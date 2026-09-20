using System.Xml.Linq;

namespace Bennewitz.Ninja.XamlQuality.ThemeAudit;

/// <summary>
/// One defined resource as the inventory holds it: its analysed value, the file that defined it
/// (<c>null</c> when a code provider supplied it), and a detached copy of the defining element so
/// a generator can carry the definition verbatim into another dictionary.
/// </summary>
public sealed record ResourceDefinition(ResourceValue Value, string? File, XElement? Element);

/// <summary>
/// Where a theme comes from and what the walker needs to read it completely: the entry file and
/// the assembly's <c>avares</c> base directory, plus the optional facts a checkout does not carry.
/// </summary>
/// <param name="EntryFile">The theme's entry — Semi's <c>Index.axaml</c>, Fluent's <c>FluentTheme.xaml</c>.</param>
/// <param name="BaseDirectory">The assembly's <c>avares</c> base, where a <c>/</c>-rooted include resolves.</param>
public sealed record ThemeSource(string EntryFile, string BaseDirectory)
{
    /// <summary>The theme's assembly, so its own <c>avares://Assembly/…</c> includes resolve.</summary>
    public string? AssemblyName { get; init; }

    /// <summary>
    /// Variant display name → the display name of the variant it inherits from. Semi's Desert
    /// inherits Light and its Aquatic/Dusk/NightSky inherit Dark, so a key defined only on the
    /// parent still counts as defined on the child, exactly as Avalonia resolves it.
    /// </summary>
    public IReadOnlyDictionary<string, string>? Inheritance { get; init; }

    /// <summary>
    /// Resource path (<c>/Strings/InvariantResources.xaml</c>, or a full <c>avares://</c> URI)
    /// → the file that supplies it, for a file the project links in from outside its directory.
    /// </summary>
    public IReadOnlyDictionary<string, string>? Links { get; init; }

    /// <summary>
    /// Code-behind element name → the keys it supplies, each with a colour literal, an alias key,
    /// or <c>null</c> when the value is not a colour. Declares a <c>ResourceProvider</c> written in
    /// code (Fluent's <c>SystemAccentColors</c>) that no AXAML file can stand in for.
    /// </summary>
    public IReadOnlyDictionary<string, IReadOnlyDictionary<string, string?>>? Providers { get; init; }

    /// <summary>
    /// Further dictionaries walked after the entry, layered over it the way an application that
    /// includes them after the theme sees them — a compat dictionary, a host's own overrides.
    /// </summary>
    public IReadOnlyList<string>? Merge { get; init; }
}

/// <summary>
/// One theme variant's effective resources: every key it defines after merging its shared base,
/// the <c>Default</c> dictionary Avalonia falls back to per key, its variant-specific keys, and
/// the keys it inherits from a parent variant. A key resolves to a colour when it is a literal or
/// an alias chain that ends in one, with brush opacities folded into the alpha; a thickness, a
/// gradient or an unresolved alias is defined but not colour-scored.
/// </summary>
public sealed class VariantInventory
{
    private readonly IReadOnlyDictionary<string, ResourceDefinition> _effective;

    internal VariantInventory(string key, string displayName, IReadOnlyDictionary<string, string> keyNamespaces,
                              IReadOnlyDictionary<string, ResourceDefinition> effective)
    {
        Key = key;
        DisplayName = displayName;
        KeyNamespaces = keyNamespaces;
        _effective = effective;
    }

    /// <summary>The raw variant key (e.g. <c>Dark</c> or <c>{x:Static semi:SemiTheme.NightSky}</c>).</summary>
    public string Key { get; }

    /// <summary>The readable variant name (e.g. <c>NightSky</c>).</summary>
    public string DisplayName { get; }

    /// <summary>The XML namespace declarations (prefix → namespace) the raw <see cref="Key"/> depends on.</summary>
    public IReadOnlyDictionary<string, string> KeyNamespaces { get; }

    /// <summary>Every key this variant defines, after base merge, Default fallback and inheritance.</summary>
    public IReadOnlyCollection<string> Keys => (IReadOnlyCollection<string>)_effective.Keys;

    /// <summary>Whether this variant defines <paramref name="key"/> at all.</summary>
    public bool Defines(string key) => _effective.ContainsKey(key);

    /// <summary>The definition of <paramref name="key"/> under this variant, or <c>null</c> when undefined.</summary>
    public ResourceDefinition? Lookup(string key)
    {
        return _effective.TryGetValue(key, out ResourceDefinition? definition) ? definition : null;
    }

    /// <summary>
    /// The colour <paramref name="key"/> resolves to under this variant, following an alias chain
    /// to its literal and multiplying every brush opacity on the way into the alpha; <c>null</c>
    /// when the key is undefined, opaque, or an alias that dangles or cycles.
    /// </summary>
    public AuditColor? Resolve(string key)
    {
        return Resolve(key, [], 1.0);
    }

    private AuditColor? Resolve(string key, HashSet<string> visiting, double opacity)
    {
        if (!_effective.TryGetValue(key, out ResourceDefinition? definition) || !visiting.Add(key))
        {
            return null;
        }

        return definition.Value switch
        {
            ResourceValue.ColorLiteral literal => WithOpacity(literal.Color, opacity * literal.Opacity),
            ResourceValue.Alias alias => Resolve(alias.TargetKey, visiting, opacity * alias.Opacity),
            _ => null,
        };
    }

    private static AuditColor WithOpacity(AuditColor color, double opacity)
    {
        return opacity >= 1.0 ? color : color with { A = (byte)Math.Round(color.A * opacity) };
    }
}

/// <summary>
/// The per-variant inventory of a theme: which keys each variant defines and what colour each
/// resolves to, built by <see cref="ThemeGraphWalker"/> walking the resource graph with a variant
/// context, then layering shared base keys, the <c>Default</c> dictionary, inherited keys and the
/// variant's own keys in Avalonia's lookup order.
/// </summary>
public sealed class ThemeInventory
{
    /// <summary>The <c>ThemeDictionaries</c> key Avalonia falls back to when a variant lacks a key.</summary>
    public const string DefaultVariant = "Default";

    private readonly IReadOnlyDictionary<string, VariantInventory> _byDisplayName;
    private readonly IReadOnlyDictionary<string, string>? _inheritance;
    private readonly VariantInventory? _baseOnly;

    private ThemeInventory(IReadOnlyList<VariantInventory> variants, VariantInventory? baseOnly,
                           IReadOnlyList<UnresolvedInclude> unresolved, IReadOnlyList<string> files,
                           IReadOnlyDictionary<string, string>? inheritance)
    {
        Variants = variants;
        Unresolved = unresolved;
        Files = files;
        _inheritance = inheritance;
        _baseOnly = baseOnly;
        _byDisplayName = variants
            .GroupBy(v => v.DisplayName, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
    }

    /// <summary>
    /// The theme's variants, in the order they are first declared. A dictionary that declares no
    /// <c>ThemeDictionaries</c> at all has exactly one, <c>Default</c>, holding its keys.
    /// </summary>
    public IReadOnlyList<VariantInventory> Variants { get; }

    /// <summary>Every include or code-behind reference the walker could not resolve.</summary>
    public IReadOnlyList<UnresolvedInclude> Unresolved { get; }

    /// <summary>Every file the walker read, in walk order — the material a digest is taken over.</summary>
    public IReadOnlyList<string> Files { get; }

    /// <summary>
    /// The variant that applies when an application requests <paramref name="displayName"/>:
    /// the exact variant, else the nearest declared ancestor along the inheritance map, else
    /// <c>Default</c> — Avalonia's own fallback — else the keys the theme defines outside any
    /// variant, which Avalonia serves whatever variant is active. Fluent and Simple declare only
    /// <c>Default</c> and <c>Dark</c>, so their <c>Light</c> is <c>Default</c>.
    /// </summary>
    public VariantInventory ForVariant(string displayName)
    {
        HashSet<string> visiting = new(StringComparer.Ordinal);
        string? current = displayName;
        while (current is not null && visiting.Add(current))
        {
            if (_byDisplayName.TryGetValue(current, out VariantInventory? variant))
            {
                return variant;
            }

            current = _inheritance is not null && _inheritance.TryGetValue(current, out string? parent) ? parent : null;
        }

        return _byDisplayName.TryGetValue(DefaultVariant, out VariantInventory? fallback) ? fallback : _baseOnly!;
    }

    /// <summary>The variant <paramref name="displayName"/> inherits from, per the inheritance map, or <c>null</c>.</summary>
    public string? ParentOf(string displayName)
    {
        return _inheritance is not null && _inheritance.TryGetValue(displayName, out string? parent) ? parent : null;
    }

    /// <summary>
    /// Builds the inventory for the theme rooted at <paramref name="entryFile"/>.
    /// <paramref name="baseDirectory"/> is the assembly's <c>avares</c> base and
    /// <paramref name="assemblyName"/> its assembly (so its own <c>avares://</c> includes resolve).
    /// <paramref name="inheritance"/> maps a variant's display name to the display name of the
    /// variant it inherits from. See <see cref="ThemeSource"/> for the full set of inputs.
    /// </summary>
    public static ThemeInventory Build(
        string entryFile,
        string baseDirectory,
        string? assemblyName = null,
        IReadOnlyDictionary<string, string>? inheritance = null)
    {
        return Build(new ThemeSource(entryFile, baseDirectory) { AssemblyName = assemblyName, Inheritance = inheritance });
    }

    /// <summary>Builds the inventory for <paramref name="source"/>.</summary>
    public static ThemeInventory Build(ThemeSource source)
    {
        WalkResult walk = ThemeGraphWalker.Walk(source);
        IReadOnlyDictionary<string, string>? inheritance = source.Inheritance;

        // Default's own keys sit under every other variant: Avalonia looks a key up in the
        // requested variant, then its inherit chain, then Default.
        IReadOnlyDictionary<string, ResourceDefinition> defaultOwn =
            walk.Own.TryGetValue(DefaultVariant, out IReadOnlyDictionary<string, ResourceDefinition>? declaredDefault)
                ? declaredDefault
                : new Dictionary<string, ResourceDefinition>(StringComparer.Ordinal);

        // A dictionary with no ThemeDictionaries still has its keys: they become its one variant,
        // Default. A theme that declares variants but no Default keeps its base keys reachable
        // through ForVariant's last fallback, without adding a variant it never declared.
        List<VariantId> variants = [.. walk.Variants];
        VariantId baseOnly = new(DefaultVariant, DefaultVariant, new Dictionary<string, string>(StringComparer.Ordinal));
        if (variants.Count == 0)
        {
            variants.Add(baseOnly);
        }

        IReadOnlyDictionary<string, ResourceDefinition> OwnOf(VariantId variant)
        {
            return walk.Own.TryGetValue(variant.Key, out IReadOnlyDictionary<string, ResourceDefinition>? own)
                ? own
                : new Dictionary<string, ResourceDefinition>(StringComparer.Ordinal);
        }

        // Lookup order, lowest first: shared base, Default, the inherit chain, the variant's own
        // keys. A root variant sits on base + Default; a child sits on its parent's effective set,
        // which already carries both. Inheritance is by display name, transitive, child over
        // parent; a cycle falls back to treating the variant as a root.
        Dictionary<string, VariantId> byDisplayName = variants
            .GroupBy(v => v.DisplayName, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        Dictionary<string, IReadOnlyDictionary<string, ResourceDefinition>> effective = new(StringComparer.Ordinal);

        IReadOnlyDictionary<string, ResourceDefinition> Effective(VariantId variant, HashSet<string> visiting)
        {
            if (effective.TryGetValue(variant.Key, out IReadOnlyDictionary<string, ResourceDefinition>? done))
            {
                return done;
            }

            Dictionary<string, ResourceDefinition> result;
            if (inheritance is not null
                && inheritance.TryGetValue(variant.DisplayName, out string? parentName)
                && byDisplayName.TryGetValue(parentName, out VariantId? parent)
                && !ReferenceEquals(parent, variant)
                && visiting.Add(variant.Key))
            {
                result = new Dictionary<string, ResourceDefinition>(Effective(parent, visiting), StringComparer.Ordinal);
            }
            else
            {
                result = new Dictionary<string, ResourceDefinition>(walk.Base, StringComparer.Ordinal);
                if (variant.Key != DefaultVariant)
                {
                    Overlay(result, defaultOwn);
                }
            }

            Overlay(result, OwnOf(variant));
            effective[variant.Key] = result;
            return result;
        }

        List<VariantInventory> inventories = variants
            .Select(v => new VariantInventory(v.Key, v.DisplayName, v.Namespaces, Effective(v, [])))
            .ToList();

        VariantInventory? baseOnlyFallback = variants.Any(v => v.Key == DefaultVariant)
            ? null
            : new VariantInventory(baseOnly.Key, baseOnly.DisplayName, baseOnly.Namespaces, walk.Base);

        return new ThemeInventory(inventories, baseOnlyFallback, walk.Unresolved, walk.Files, inheritance);
    }

    private static void Overlay(Dictionary<string, ResourceDefinition> target, IReadOnlyDictionary<string, ResourceDefinition> source)
    {
        foreach ((string key, ResourceDefinition definition) in source)
        {
            target[key] = definition;
        }
    }
}
