using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace Bennewitz.Ninja.XamlQuality.ThemeAudit;

/// <summary>How a generated key was produced.</summary>
public enum CompatHow
{
    /// <summary>The reviewed mapping named a target token or literal.</summary>
    Mapped,

    /// <summary>The source theme's own definition was copied verbatim (an alias, a thickness, a literal brush).</summary>
    Copied,

    /// <summary>The source's alias dangled after mapping, so the colour it resolves to was written as a literal — a review item.</summary>
    Literal,

    /// <summary>The target theme's own variant already defined the key; its definition was restored over the inherited compat entry.</summary>
    Restored,
}

/// <summary>One generated key.</summary>
public sealed record CompatEntry(string Variant, string Key, CompatHow How, string Detail);

/// <summary>One key left out, and why.</summary>
public sealed record CompatSkipped(string Variant, string Key, string Reason);

/// <summary>A generated dictionary with its ledger.</summary>
public sealed record CompatGeneration(string Xml, IReadOnlyList<CompatEntry> Entries, IReadOnlyList<CompatSkipped> Skipped);

/// <summary>
/// Generates a compat resource dictionary: for each target variant, every key the source theme
/// defines that the target lacks, so a control templated for the source theme (AvaloniaEdit's
/// search panel under Fluent) stops going invisible under the target (Semi). A key in the
/// reviewed mapping becomes an alias to the target's own token; an alias or a plain value is
/// copied verbatim within the copy policy; a copied definition whose references would dangle is
/// replaced by the literal colour it resolves to and flagged for review. A key the target's own
/// high-contrast variant defines is restored there over the inherited entry, so the compat
/// dictionary never shadows the target theme. Generated, never hand-edited.
/// </summary>
public static class CompatGenerator
{
    private static readonly XNamespace Avalonia = "https://github.com/avaloniaui";
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    /// <summary>
    /// Generates the dictionary carrying <paramref name="from"/>'s keys into <paramref name="to"/>.
    /// <paramref name="fromName"/> and <paramref name="toName"/> appear in the file's comment.
    /// <paramref name="variantKeys"/>, when given, keys the target's custom variants through that
    /// type's members instead of the target theme's own <c>{x:Static}</c> expression.
    /// </summary>
    public static CompatGeneration Generate(ThemeInventory from, ThemeInventory to, CompatMapping mapping, string fromName, string toName,
                                            VariantKeyStyle? variantKeys = null)
    {
        List<CompatEntry> entries = [];
        List<CompatSkipped> skipped = [];
        Dictionary<string, Dictionary<string, XElement>> generated = new(StringComparer.Ordinal);
        List<(string Variant, string RawKey, IReadOnlyDictionary<string, string> Namespaces)> order = [];

        // The mapped variants, in mapping order.
        foreach ((string toVariant, string fromVariant) in mapping.Variants)
        {
            VariantInventory source = from.ForVariant(fromVariant);
            VariantInventory target = to.ForVariant(toVariant);
            Dictionary<string, XElement> elements = GenerateVariant(toVariant, source, target, mapping, entries, skipped);
            generated[toVariant] = elements;
            order.Add((toVariant, RawKeyOf(to, toVariant), NamespacesOf(to, toVariant)));
        }

        // The target's other declared variants: restore whatever they define themselves that the
        // compat entry inherited from their parent would otherwise shadow.
        foreach (VariantInventory declared in to.Variants)
        {
            if (mapping.Variants.ContainsKey(declared.DisplayName) || declared.DisplayName == ThemeInventory.DefaultVariant)
            {
                continue;
            }

            string? parent = NearestMappedAncestor(to, declared.DisplayName, mapping);
            if (parent is null || !generated.TryGetValue(parent, out Dictionary<string, XElement>? parentElements))
            {
                continue;
            }

            VariantInventory parentInventory = to.ForVariant(parent);
            Dictionary<string, XElement> restored = new(StringComparer.Ordinal);
            foreach (string key in parentElements.Keys.Order(StringComparer.Ordinal))
            {
                ResourceDefinition? own = declared.Lookup(key);
                if (own is null || ReferenceEquals(own, parentInventory.Lookup(key)))
                {
                    continue;
                }

                if (own.Element is null)
                {
                    skipped.Add(new CompatSkipped(declared.DisplayName, key, $"{toName} {declared.DisplayName} defines it in code; the inherited compat entry will shadow it"));
                    continue;
                }

                restored[key] = new XElement(own.Element);
                entries.Add(new CompatEntry(declared.DisplayName, key, CompatHow.Restored, $"{toName} {declared.DisplayName}'s own definition"));
            }

            if (restored.Count > 0)
            {
                generated[declared.DisplayName] = restored;
                order.Add((declared.DisplayName, declared.Key, declared.KeyNamespaces));
            }
        }

        // A custom variant key is an {x:Static} the compiling project must be able to resolve;
        // re-key it through the caller's stand-in type when one is given.
        if (variantKeys is not null)
        {
            Dictionary<string, string> standIn = new(StringComparer.Ordinal) { [variantKeys.Prefix] = variantKeys.Namespace };
            for (int i = 0; i < order.Count; i++)
            {
                (string variant, string rawKey, _) = order[i];
                if (rawKey.StartsWith('{'))
                {
                    order[i] = (variant, "{x:Static " + variantKeys.Prefix + ":" + variantKeys.Type + "." + variant + "}", standIn);
                }
            }
        }

        string xml = Render(order, generated, from, mapping, fromName, toName);
        return new CompatGeneration(xml, entries, skipped);
    }

    private static Dictionary<string, XElement> GenerateVariant(
        string variant, VariantInventory source, VariantInventory target, CompatMapping mapping,
        List<CompatEntry> entries, List<CompatSkipped> skipped)
    {
        HashSet<string> skip = new(mapping.Skip, StringComparer.Ordinal);
        List<string> missing = source.Keys
            .Where(k => !target.Defines(k) && !skip.Contains(k))
            .Order(StringComparer.Ordinal)
            .ToList();

        Dictionary<string, XElement> elements = new(StringComparer.Ordinal);
        Dictionary<string, CompatEntry> pending = new(StringComparer.Ordinal);

        foreach (string key in missing)
        {
            ResourceDefinition definition = source.Lookup(key)!;

            if (mapping.Keys.TryGetValue(key, out MappingTarget? mapped))
            {
                if (TryMap(key, definition, mapped, target, out XElement? element, out string? detail, out string? failure))
                {
                    elements[key] = element!;
                    pending[key] = new CompatEntry(variant, key, CompatHow.Mapped, detail!);
                }
                else
                {
                    skipped.Add(new CompatSkipped(variant, key, failure!));
                }

                continue;
            }

            if (definition.Element is null)
            {
                if (definition.Value is ResourceValue.ColorLiteral provided)
                {
                    elements[key] = ColorElement(key, provided.Color);
                    pending[key] = new CompatEntry(variant, key, CompatHow.Literal, $"supplied by code as {provided.Color}");
                }
                else
                {
                    skipped.Add(new CompatSkipped(variant, key, "supplied by code and not a colour"));
                }

                continue;
            }

            if (!MayCopy(definition.Element, mapping.Copy, out string? why))
            {
                skipped.Add(new CompatSkipped(variant, key, why!));
                continue;
            }

            elements[key] = new XElement(definition.Element);
            pending[key] = new CompatEntry(variant, key, CompatHow.Copied, ElementName(definition.Element));
        }

        // Closure: every key a generated element references must exist in the target or in this
        // dictionary, or the element would throw at load. A dangling colour becomes its literal;
        // anything else is dropped, and dropping can dangle another, so repeat until stable.
        bool changed = true;
        while (changed)
        {
            changed = false;
            foreach (string key in elements.Keys.Order(StringComparer.Ordinal).ToList())
            {
                XElement element = elements[key];
                List<string> dangling = ReferencedKeys(element)
                    .Where(r => !target.Defines(r) && !elements.ContainsKey(r))
                    .Distinct(StringComparer.Ordinal)
                    .Order(StringComparer.Ordinal)
                    .ToList();
                if (dangling.Count == 0)
                {
                    continue;
                }

                changed = true;
                AuditColor? colour = source.Resolve(key);
                if (colour is { } literal)
                {
                    elements[key] = IsBrush(element) ? BrushElement(key, literal) : ColorElement(key, literal);
                    pending[key] = new CompatEntry(variant, key, CompatHow.Literal,
                        $"references {string.Join(", ", dangling)} which nothing defines; written as {literal}");
                }
                else
                {
                    elements.Remove(key);
                    pending.Remove(key);
                    skipped.Add(new CompatSkipped(variant, key, $"references {string.Join(", ", dangling)} which nothing defines, and it is not a colour"));
                }
            }
        }

        entries.AddRange(pending.Values.OrderBy(e => e.Key, StringComparer.Ordinal));
        return elements;
    }

    private static bool TryMap(string key, ResourceDefinition definition, MappingTarget mapped, VariantInventory target,
                               out XElement? element, out string? detail, out string? failure)
    {
        element = null;
        detail = null;
        failure = null;
        bool sourceIsBrush = definition.Element is null ? false : IsBrush(definition.Element);

        if (AuditColor.TryParse(mapped.To, out AuditColor literal))
        {
            element = sourceIsBrush ? BrushElement(key, literal) : ColorElement(key, literal);
            detail = $"literal {literal}" + Why(mapped);
            return true;
        }

        ResourceDefinition? targetDefinition = target.Lookup(mapped.To);
        if (targetDefinition is null)
        {
            failure = $"mapped to {mapped.To}, which the target theme does not define under this variant";
            return false;
        }

        bool targetIsBrush = targetDefinition.Element is not null && IsBrush(targetDefinition.Element);
        if (sourceIsBrush)
        {
            element = targetIsBrush
                ? AliasElement(key, mapped.To)
                : new XElement(Avalonia + "SolidColorBrush",
                    new XAttribute(Xaml + "Key", key),
                    new XAttribute("Color", "{DynamicResource " + mapped.To + "}"));
        }
        else
        {
            if (targetIsBrush)
            {
                failure = $"mapped to {mapped.To}, a brush, but the source defines a colour; map a colour key to a colour key";
                return false;
            }

            element = AliasElement(key, mapped.To);
        }

        detail = mapped.To + Why(mapped);
        return true;
    }

    private static string Why(MappingTarget mapped)
    {
        return mapped.Why is null ? string.Empty : " — " + mapped.Why;
    }

    private static bool MayCopy(XElement element, CopyPolicy policy, out string? why)
    {
        string local = element.Name.LocalName;
        if (policy.SkipElements.Contains(local, StringComparer.Ordinal))
        {
            why = $"{local} is not copied by policy";
            return false;
        }

        string ns = element.Name.NamespaceName;
        if (ns != Avalonia.NamespaceName && ns != Xaml.NamespaceName && !policy.Namespaces.Contains(ns, StringComparer.Ordinal))
        {
            why = $"{local} is in namespace {ns}, which the copy policy does not allow";
            return false;
        }

        why = null;
        return true;
    }

    private static bool IsBrush(XElement element)
    {
        return element.Name.LocalName.EndsWith("Brush", StringComparison.Ordinal)
               || (element.Name.LocalName == "StaticResource" && element.Attribute("ResourceKey")?.Value.EndsWith("Brush", StringComparison.Ordinal) == true);
    }

    private static XElement ColorElement(string key, AuditColor colour)
    {
        return new XElement(Avalonia + "Color", new XAttribute(Xaml + "Key", key), colour.ToString());
    }

    private static XElement BrushElement(string key, AuditColor colour)
    {
        return new XElement(Avalonia + "SolidColorBrush", new XAttribute(Xaml + "Key", key), new XAttribute("Color", colour.ToString()));
    }

    private static XElement AliasElement(string key, string target)
    {
        return new XElement(Avalonia + "StaticResource", new XAttribute(Xaml + "Key", key), new XAttribute("ResourceKey", target));
    }

    /// <summary>Every key an element (and its descendants) reaches for: <c>ResourceKey</c> on an alias element, and any markup reference in an attribute.</summary>
    internal static IEnumerable<string> ReferencedKeys(XElement element)
    {
        foreach (XElement node in element.DescendantsAndSelf())
        {
            if (node.Name.LocalName is "StaticResource" or "DynamicResource"
                && node.Attribute("ResourceKey")?.Value is { Length: > 0 } resourceKey)
            {
                yield return resourceKey;
            }

            foreach (XAttribute attribute in node.Attributes())
            {
                if (attribute.IsNamespaceDeclaration)
                {
                    continue;
                }

                foreach ((ReferenceKind _, string key) in ResourceReferenceScanner.ExtractMarkupReferences(attribute.Value))
                {
                    yield return key;
                }
            }
        }
    }

    private static string ElementName(XElement element)
    {
        return element.Name.NamespaceName == Avalonia.NamespaceName
            ? element.Name.LocalName
            : element.Name.NamespaceName + ":" + element.Name.LocalName;
    }

    private static string RawKeyOf(ThemeInventory theme, string displayName)
    {
        return theme.Variants.FirstOrDefault(v => v.DisplayName == displayName)?.Key ?? displayName;
    }

    private static IReadOnlyDictionary<string, string> NamespacesOf(ThemeInventory theme, string displayName)
    {
        return theme.Variants.FirstOrDefault(v => v.DisplayName == displayName)?.KeyNamespaces
               ?? new Dictionary<string, string>(StringComparer.Ordinal);
    }

    /// <summary>
    /// The mapped variant whose compat entries an application would reach when it requests
    /// <paramref name="variant"/>: up the inheritance chain to the first mapped variant, else
    /// Default when that is mapped — Avalonia's own fallback, so Semi's Desert (inherits Light,
    /// which is not mapped) lands on the Default dictionary.
    /// </summary>
    private static string? NearestMappedAncestor(ThemeInventory to, string variant, CompatMapping mapping)
    {
        HashSet<string> visiting = new(StringComparer.Ordinal);
        string? current = to.ParentOf(variant);
        while (current is not null && visiting.Add(current))
        {
            if (mapping.Variants.ContainsKey(current))
            {
                return current;
            }

            current = to.ParentOf(current);
        }

        return mapping.Variants.ContainsKey(ThemeInventory.DefaultVariant) ? ThemeInventory.DefaultVariant : null;
    }

    private static string Render(
        List<(string Variant, string RawKey, IReadOnlyDictionary<string, string> Namespaces)> order,
        Dictionary<string, Dictionary<string, XElement>> generated,
        ThemeInventory from, CompatMapping mapping, string fromName, string toName)
    {
        // Prefixes: XAML's x, then every prefix the source files declare (first spelling of a
        // namespace wins, a later prefix clash gets a numeric suffix), then the variant keys' own.
        Dictionary<string, string> prefixByNamespace = new(StringComparer.Ordinal) { [Xaml.NamespaceName] = "x" };
        HashSet<string> usedPrefixes = new(StringComparer.Ordinal) { "x" };

        void Declare(string prefix, string ns)
        {
            if (prefixByNamespace.ContainsKey(ns) || ns == Avalonia.NamespaceName)
            {
                return;
            }

            string candidate = prefix;
            for (int i = 2; usedPrefixes.Contains(candidate); i++)
            {
                candidate = prefix + i;
            }

            prefixByNamespace[ns] = candidate;
            usedPrefixes.Add(candidate);
        }

        foreach (string file in from.Files)
        {
            XElement? fileRoot;
            try
            {
                fileRoot = XDocument.Load(file).Root;
            }
            catch (XmlException)
            {
                continue;
            }

            foreach (XAttribute declaration in fileRoot?.Attributes().Where(a => a.IsNamespaceDeclaration && a.Name.LocalName != "xmlns") ?? [])
            {
                Declare(declaration.Name.LocalName, declaration.Value);
            }
        }

        foreach ((_, _, IReadOnlyDictionary<string, string> namespaces) in order)
        {
            foreach ((string prefix, string ns) in namespaces)
            {
                Declare(prefix, ns);
            }
        }

        // Only namespaces actually used by the output are declared.
        HashSet<string> usedNamespaces = new(StringComparer.Ordinal) { Xaml.NamespaceName };
        foreach (Dictionary<string, XElement> elements in generated.Values)
        {
            foreach (XElement element in elements.Values)
            {
                foreach (XElement node in element.DescendantsAndSelf())
                {
                    usedNamespaces.Add(node.Name.NamespaceName);
                    foreach (XAttribute attribute in node.Attributes().Where(a => !a.IsNamespaceDeclaration))
                    {
                        usedNamespaces.Add(attribute.Name.NamespaceName);
                    }
                }
            }
        }

        foreach ((_, _, IReadOnlyDictionary<string, string> namespaces) in order)
        {
            foreach (string ns in namespaces.Values)
            {
                usedNamespaces.Add(ns);
            }
        }

        XElement root = new(Avalonia + "ResourceDictionary");
        root.Add(new XAttribute("xmlns", Avalonia.NamespaceName));
        foreach ((string ns, string prefix) in prefixByNamespace.Where(kv => usedNamespaces.Contains(kv.Key)).OrderBy(kv => kv.Value == "x" ? 0 : 1).ThenBy(kv => kv.Value, StringComparer.Ordinal))
        {
            root.Add(new XAttribute(XNamespace.Xmlns + prefix, ns));
        }

        // Sanity: an element may use a namespace the source never declared with a prefix (it
        // came from a default declaration); give it one so the output stays readable.
        foreach (string ns in usedNamespaces.Where(n => n != Avalonia.NamespaceName && n.Length > 0 && !prefixByNamespace.ContainsKey(n)).Order(StringComparer.Ordinal))
        {
            Declare("ns", ns);
            root.Add(new XAttribute(XNamespace.Xmlns + prefixByNamespace[ns], ns));
        }

        XElement themeDictionaries = new(Avalonia + "ResourceDictionary.ThemeDictionaries");
        foreach ((string variant, string rawKey, _) in order)
        {
            XElement dictionary = new(Avalonia + "ResourceDictionary", new XAttribute(Xaml + "Key", rawKey));
            foreach ((string _, XElement element) in generated[variant].OrderBy(kv => kv.Key, StringComparer.Ordinal))
            {
                dictionary.Add(element);
            }

            themeDictionaries.Add(dictionary);
        }

        root.Add(themeDictionaries);

        XDocument document = new(
            new XComment(
                $" Generated by theme-audit compat: the keys {fromName} defines that {toName} lacks, mapped by {mapping.Name}. " +
                "Do not edit; regenerate with `theme-audit compat`. Include it after the theme where a control templated " +
                $"for {fromName} is hosted under {toName}. "),
            root);

        XmlWriterSettings settings = new()
        {
            Indent = true,
            IndentChars = "  ",
            NewLineChars = "\n",
            NewLineHandling = NewLineHandling.Replace,
            OmitXmlDeclaration = true,
            Encoding = new UTF8Encoding(false),
        };

        StringBuilder sb = new();
        using (XmlWriter writer = XmlWriter.Create(sb, settings))
        {
            document.Save(writer);
        }

        sb.Append('\n');
        return sb.ToString();
    }
}
