using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace Bennewitz.Ninja.XamlQuality;

/// <summary>A resource defined in the scan: the element carrying the key, and the file it is in.</summary>
/// <param name="File">The file the definition is in.</param>
/// <param name="Element">The element carrying the <c>x:Key</c>.</param>
internal sealed record Definition(XamlFile File, XElement Element)
{
    /// <summary>1-based line of the definition, or <c>null</c>.</summary>
    internal int? Line => (Element as IXmlLineInfo).HasLineInfo() ? ((IXmlLineInfo)Element).LineNumber : null;

    /// <summary>Where it is, as a reader would look for it.</summary>
    internal string Where => Line is { } line ? $"{File.RelativePath}:{line}" : File.RelativePath;
}

/// <summary>How far a key resolved.</summary>
internal enum Reach
{
    /// <summary>One definition, reached the way the framework would reach it, or the only one there is.</summary>
    Found,

    /// <summary>No definition in the scan: the key belongs to a theme or package outside it.</summary>
    NotInScan,

    /// <summary>More than one definition outside the lookup's own scope, and nothing to choose between them.</summary>
    Ambiguous,

    /// <summary>
    /// Implicit keys only: definitions exist, but none in a scope markup shows reaching this element.
    /// Whether one does depends on where the view is placed at runtime.
    /// </summary>
    MayReach,
}

/// <summary>The outcome of looking a key up from somewhere in the markup.</summary>
/// <param name="Reach">How far it resolved.</param>
/// <param name="Definition">The definition, when <paramref name="Reach"/> is <see cref="Reach.Found"/>.</param>
/// <param name="Others">The definitions that exist but were not reached, for the other outcomes.</param>
internal sealed record Lookup(Reach Reach, Definition? Definition, IReadOnlyList<Definition> Others);

/// <summary>A <c>Setter</c> inside a top-level <c>Style</c>, and the type names its selector mentions.</summary>
/// <param name="File">The file the style is in.</param>
/// <param name="Setter">The setter.</param>
/// <param name="Property">The property it sets, without an owner prefix.</param>
/// <param name="Mentions">Every identifier in the style's selector.</param>
internal sealed record StyleSetter(XamlFile File, XElement Setter, string Property, IReadOnlySet<string> Mentions)
{
    /// <summary>Where it is, as a reader would look for it.</summary>
    internal string Where => (Setter as IXmlLineInfo).HasLineInfo()
        ? $"{File.RelativePath}:{((IXmlLineInfo)Setter).LineNumber}"
        : File.RelativePath;
}

/// <summary>
/// Where a resource key in the scan resolves from a given element, following the framework's lookup
/// as far as markup shows it.
/// </summary>
/// <remarks>
/// <para>
/// ⭐ <b>The lookup a framework makes, walked in markup.</b> From an element: its own
/// <c>Resources</c>, then its <c>Styles</c>' resources, then each ancestor's in turn, then the
/// application's <c>Resources</c> and <c>Styles</c>. A dictionary is searched before the
/// dictionaries merged into it, and an include is followed to the file it names when that file is in
/// the scan. That is Avalonia 12's order, read in <c>ResourceNodeExtensions.TryFindResource</c>.
/// </para>
/// <para>
/// ⚠ <b>Markup does not say where a view is placed.</b> An ancestor in another file, the view a
/// <c>UserControl</c> is hosted in, is invisible from the file being read. So a key found nowhere
/// in scope but defined exactly once in the scan is taken to be that definition, because the markup
/// referencing it had to find it somewhere. An implicit key, a type's own theme, is not guessed at
/// that way: one defined in another file is reported as <see cref="Reach.MayReach"/>, and one in the
/// same file but outside the element's ancestors does not reach it, since a file's containment is
/// visible. Measured on Avalonia 12.1.3: an implicit theme in a sibling's resources left the list's
/// rows focusable.
/// </para>
/// <para>
/// ⛔ <b>An implicit theme is keyed <c>{x:Type T}</c>.</b> A <c>ControlTheme</c> with no <c>x:Key</c>
/// does not compile in Avalonia 12.1.3 (<c>AVLN3000</c>), so no key is invented for one.
/// </para>
/// </remarks>
internal sealed partial class ResourceScope
{
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    private readonly IReadOnlyList<XamlFile> _files;
    private readonly Dictionary<XDocument, XamlFile> _fileOf = [];
    private readonly Dictionary<string, List<Definition>> _definitions = new(StringComparer.Ordinal);
    private readonly List<StyleSetter> _styleSetters = [];
    private readonly List<XamlFile> _applications = [];

    internal ResourceScope(XamlScanContext context)
    {
        _files = [.. context.ParsedFiles];

        foreach (XamlFile file in _files)
        {
            XDocument document = file.Document!;
            _fileOf[document] = file;

            if (document.Root?.Name.LocalName == "Application")
            {
                _applications.Add(file);
            }

            foreach (XElement element in document.Descendants())
            {
                if (element.Attribute(Xaml + "Key") is { } key && NormalizeKey(key.Value) is { } normalized)
                {
                    if (!_definitions.TryGetValue(normalized, out List<Definition>? definitions))
                    {
                        definitions = [];
                        _definitions[normalized] = definitions;
                    }

                    definitions.Add(new Definition(file, element));
                }

                if (element.Name.LocalName == "Style"
                    && element.Attribute("Selector") is { } selector
                    && !element.Ancestors().Any(ancestor => ancestor.Name.LocalName == "ControlTheme"))
                {
                    HashSet<string> mentions = [.. Identifier().Matches(selector.Value).Select(match => match.Value)];
                    foreach (XElement setter in element.Elements().Where(child => child.Name.LocalName == "Setter"))
                    {
                        if (setter.Attribute("Property")?.Value is { } property)
                        {
                            _styleSetters.Add(new StyleSetter(file, setter, PropertyName(property), mentions));
                        }
                    }
                }
            }
        }
    }

    /// <summary>The key an implicit theme for <paramref name="typeName"/> is stored under, normalised.</summary>
    internal static string ImplicitKey(string typeName) => "{x:Type " + typeName + "}";

    /// <summary>Whether <paramref name="key"/> is an implicit, type-keyed one.</summary>
    internal static bool IsImplicit(string key) => key.StartsWith("{x:Type ", StringComparison.Ordinal);

    /// <summary>
    /// A key as written in <c>x:Key</c>, normalised so that <c>{x:Type ListBoxItem}</c>,
    /// <c>{x:Type local:ListBoxItem}</c> and <c>{x:Type TypeName=ListBoxItem}</c> compare equal.
    /// </summary>
    internal static string? NormalizeKey(string raw)
    {
        string key = raw.Trim();
        if (key.Length == 0)
        {
            return null;
        }

        if (!key.StartsWith('{') || !key.EndsWith('}'))
        {
            return key;
        }

        string[] parts = key[1..^1].Trim().Split((char[])[' ', '\t', '\r', '\n'], 2, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 2 && LocalPart(parts[0]) == "Type")
        {
            string argument = parts[1].Trim();
            if (argument.StartsWith("TypeName=", StringComparison.Ordinal))
            {
                argument = argument["TypeName=".Length..].Trim();
            }

            return ImplicitKey(LocalPart(argument));
        }

        return key;
    }

    /// <summary>
    /// The key a resource reference names: <c>{StaticResource K}</c>, <c>{DynamicResource K}</c>,
    /// <c>{StaticResource ResourceKey=K}</c> or <c>{StaticResource {x:Type T}}</c>; <c>null</c> for
    /// anything else.
    /// </summary>
    internal static string? ReferencedKey(string value)
    {
        string text = value.Trim();
        if (!text.StartsWith('{') || !text.EndsWith('}'))
        {
            return null;
        }

        string inner = text[1..^1].Trim();
        int space = inner.IndexOfAny([' ', '\t', '\r', '\n']);
        if (space < 0 || LocalPart(inner[..space]) is not ("StaticResource" or "DynamicResource"))
        {
            return null;
        }

        string argument = inner[(space + 1)..].Trim();
        if (argument.StartsWith("ResourceKey=", StringComparison.Ordinal))
        {
            argument = argument["ResourceKey=".Length..].Trim();
        }

        return NormalizeKey(argument);
    }

    /// <summary>The name of a property as a setter or a property element writes it, without its owner.</summary>
    internal static string PropertyName(string written)
    {
        string name = written.Trim();
        int dot = name.LastIndexOf('.');
        return dot >= 0 ? name[(dot + 1)..] : name;
    }

    /// <summary>The file an element is in, or <c>null</c> when it is not in the scan.</summary>
    internal XamlFile? FileOf(XElement element) =>
        element.Document is { } document && _fileOf.TryGetValue(document, out XamlFile? file) ? file : null;

    /// <summary>
    /// Every top-level style setter for one of <paramref name="properties"/> whose selector mentions
    /// one of <paramref name="typeNames"/>.
    /// </summary>
    /// <remarks>
    /// ⚠ <b>A mention, not a match.</b> Selectors are not evaluated; a style is returned when its
    /// selector names the type anywhere. That is deliberate for its one use, which is to stop and say
    /// "a style may change this" rather than to decide what the style does.
    /// </remarks>
    internal IReadOnlyList<StyleSetter> StylesSetting(IReadOnlyCollection<string> typeNames, params string[] properties) =>
        [.. _styleSetters.Where(setter => properties.Contains(setter.Property, StringComparer.Ordinal)
                                          && setter.Mentions.Overlaps(typeNames))];

    /// <summary>Where <paramref name="key"/> resolves from <paramref name="from"/>.</summary>
    /// <param name="key">A normalised key.</param>
    /// <param name="from">The element the lookup starts at.</param>
    /// <param name="exclude">
    /// A definition the lookup must not return, which is the element being resolved when a theme's
    /// <c>BasedOn</c> names the key the theme itself is stored under.
    /// </param>
    internal Lookup Find(string key, XElement from, XElement? exclude = null)
    {
        if (FileOf(from) is { } file)
        {
            for (XElement? scope = from; scope is not null; scope = scope.Parent)
            {
                if (SearchElement(key, scope, file, exclude, []) is { } hit)
                {
                    return new Lookup(Reach.Found, hit, []);
                }
            }
        }

        foreach (XamlFile application in _applications)
        {
            if (SearchElement(key, application.Document!.Root!, application, exclude, []) is { } hit)
            {
                return new Lookup(Reach.Found, hit, []);
            }
        }

        List<Definition> others = _definitions.TryGetValue(key, out List<Definition>? all)
            ? [.. all.Where(definition => definition.Element != exclude)]
            : [];

        if (IsImplicit(key))
        {
            // Inside one file, containment is visible: a definition the walk above did not pass through
            // sits beside the element, not above it, and cannot reach it. Only another file's might.
            XamlFile? home = FileOf(from);
            others = [.. others.Where(definition => definition.File != home)];
            return others.Count == 0
                ? new Lookup(Reach.NotInScan, null, [])
                : new Lookup(Reach.MayReach, null, others);
        }

        if (others.Count == 0)
        {
            return new Lookup(Reach.NotInScan, null, []);
        }

        return others.Count == 1
            ? new Lookup(Reach.Found, others[0], [])
            : new Lookup(Reach.Ambiguous, null, others);
    }

    /// <summary>The key an element is stored under, normalised, or <c>null</c>.</summary>
    private static string? KeyOf(XElement element) =>
        element.Attribute(Xaml + "Key") is { } key ? NormalizeKey(key.Value) : null;

    /// <summary>An element's own resources and its styles' resources, and itself when it is a dictionary.</summary>
    private Definition? SearchElement(string key, XElement element, XamlFile file, XElement? exclude, HashSet<XamlFile> visited)
    {
        foreach (XElement resources in element.Elements().Where(child => child.Name.LocalName.EndsWith(".Resources", StringComparison.Ordinal)))
        {
            if (SearchDictionary(key, resources, file, exclude, visited) is { } hit)
            {
                return hit;
            }
        }

        foreach (XElement styles in element.Elements().Where(child => child.Name.LocalName.EndsWith(".Styles", StringComparison.Ordinal)))
        {
            if (SearchStyles(key, styles, file, exclude, visited) is { } hit)
            {
                return hit;
            }
        }

        return element.Name.LocalName switch
        {
            "ResourceDictionary" => SearchDictionary(key, element, file, exclude, visited),
            "Styles" or "Style" => SearchStyles(key, element, file, exclude, visited),
            _ => null,
        };
    }

    /// <summary>A dictionary's own entries, then the dictionaries merged into it, last merged first.</summary>
    private Definition? SearchDictionary(string key, XElement dictionary, XamlFile file, XElement? exclude, HashSet<XamlFile> visited)
    {
        foreach (XElement entry in dictionary.Elements())
        {
            if (entry != exclude && KeyOf(entry) == key)
            {
                return new Definition(file, entry);
            }
        }

        foreach (XElement nested in dictionary.Elements().Where(child => child.Name.LocalName == "ResourceDictionary"))
        {
            if (SearchDictionary(key, nested, file, exclude, visited) is { } hit)
            {
                return hit;
            }
        }

        foreach (XElement merged in dictionary.Elements()
                     .Where(child => child.Name.LocalName.EndsWith(".MergedDictionaries", StringComparison.Ordinal))
                     .SelectMany(slot => slot.Elements().Reverse()))
        {
            Definition? hit = merged.Name.LocalName switch
            {
                "ResourceDictionary" => SearchDictionary(key, merged, file, exclude, visited),
                "ResourceInclude" or "MergeResourceInclude" => SearchInclude(key, merged, file, exclude, visited),
                _ => null,
            };

            if (hit is not null)
            {
                return hit;
            }
        }

        return null;
    }

    /// <summary>The resources a styles collection carries, its own and those of the styles in it, last first.</summary>
    private Definition? SearchStyles(string key, XElement styles, XamlFile file, XElement? exclude, HashSet<XamlFile> visited)
    {
        foreach (XElement resources in styles.Elements().Where(child => child.Name.LocalName.EndsWith(".Resources", StringComparison.Ordinal)))
        {
            if (SearchDictionary(key, resources, file, exclude, visited) is { } hit)
            {
                return hit;
            }
        }

        foreach (XElement style in styles.Elements().Reverse())
        {
            Definition? hit = style.Name.LocalName switch
            {
                "Style" or "Styles" => SearchStyles(key, style, file, exclude, visited),
                "StyleInclude" => SearchInclude(key, style, file, exclude, visited),
                _ => null,
            };

            if (hit is not null)
            {
                return hit;
            }
        }

        return null;
    }

    /// <summary>The file an include names, searched as the kind of root it has.</summary>
    private Definition? SearchInclude(string key, XElement include, XamlFile file, XElement? exclude, HashSet<XamlFile> visited)
    {
        if (include.Attribute("Source")?.Value is not { } source
            || ResolveSource(source, file) is not { } target
            || !visited.Add(target))
        {
            return null;
        }

        return SearchElement(key, target.Document!.Root!, target, exclude, visited);
    }

    /// <summary>
    /// The scanned file an include's <c>Source</c> names, or <c>null</c> when none does, or more than one might.
    /// </summary>
    /// <remarks>
    /// ⚠ A <c>/</c>-rooted or <c>avares://</c> source is rooted at a project the scan does not know
    /// the boundaries of, so it is matched by the path it ends with. When that matches more than one
    /// file, the one under a folder named for the assembly wins, and otherwise nothing does.
    /// </remarks>
    private XamlFile? ResolveSource(string source, XamlFile including)
    {
        string text = source.Trim();
        string? assembly = null;
        string relative;

        if (text.StartsWith("avares://", StringComparison.OrdinalIgnoreCase))
        {
            string rest = text["avares://".Length..];
            int slash = rest.IndexOf('/');
            if (slash < 0)
            {
                return null;
            }

            assembly = rest[..slash];
            relative = rest[(slash + 1)..];
        }
        else if (text.StartsWith('/'))
        {
            relative = text[1..];
        }
        else
        {
            string full = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(including.Path) ?? string.Empty, text));
            return _files.FirstOrDefault(candidate => string.Equals(
                Path.GetFullPath(candidate.Path), full, StringComparison.OrdinalIgnoreCase));
        }

        relative = relative.Replace('\\', '/').TrimStart('/');
        XamlFile[] matches = [.. _files.Where(candidate =>
            candidate.RelativePath.Equals(relative, StringComparison.OrdinalIgnoreCase)
            || candidate.RelativePath.EndsWith("/" + relative, StringComparison.OrdinalIgnoreCase))];

        if (matches.Length > 1 && assembly is not null)
        {
            matches = [.. matches.Where(candidate =>
                ("/" + candidate.RelativePath).Contains("/" + assembly + "/", StringComparison.OrdinalIgnoreCase))];
        }

        return matches.Length == 1 ? matches[0] : null;
    }

    private static string LocalPart(string qualified)
    {
        int colon = qualified.LastIndexOf(':');
        return colon >= 0 ? qualified[(colon + 1)..] : qualified;
    }

    [GeneratedRegex("[A-Za-z_][A-Za-z0-9_]*")]
    private static partial Regex Identifier();
}
