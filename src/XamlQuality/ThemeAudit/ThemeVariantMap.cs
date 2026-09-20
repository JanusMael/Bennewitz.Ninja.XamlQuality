using System.Xml;
using System.Xml.Linq;

namespace Bennewitz.Ninja.XamlQuality.ThemeAudit;

/// <summary>
/// One theme variant and the include roots that supply its variant-specific keys. For a variant
/// declared as <c>&lt;ResourceInclude x:Key="Dark" Source="…"/&gt;</c> the root is the resolved
/// file; for one declared inline (<c>&lt;ResourceDictionary x:Key="Dark"&gt;…</c>) there is no
/// separate root — those keys carry the variant on themselves and are read from the entry file.
/// </summary>
public sealed record ThemeVariant(string Key, IReadOnlyList<string> Roots, bool Inline)
{
    /// <summary>
    /// A readable name: the trailing member of an <c>{x:Static ns:Type.Member}</c> key (Semi's
    /// high-contrast variants are declared that way), otherwise the key verbatim.
    /// </summary>
    public string DisplayName => DisplayNameOf(Key);

    /// <summary>The readable name for a raw variant key. Shared with the graph walker.</summary>
    public static string DisplayNameOf(string key)
    {
        if (key.StartsWith('{') && key.Contains("x:Static", StringComparison.Ordinal))
        {
            int dot = key.LastIndexOf('.');
            int end = key.IndexOf('}', dot < 0 ? 0 : dot);
            if (dot >= 0 && end > dot)
            {
                return key[(dot + 1)..end].Trim();
            }
        }

        return key;
    }
}

/// <summary>
/// A theme entry file decomposed into the variants it declares and the include roots shared by
/// every variant, with any include that could not be resolved. The <see cref="SharedRoots"/>
/// always include the entry file itself, whose base-level (non-variant) resources apply to all
/// variants; a variant key the entry defines inline is still routed to its variant by the
/// definition scan, so nothing is lost by scanning the entry as shared.
/// </summary>
public sealed record ThemeVariantMap(
    IReadOnlyList<string> SharedRoots,
    IReadOnlyList<ThemeVariant> Variants,
    IReadOnlyList<UnresolvedInclude> Unresolved);

/// <summary>Parses a theme entry file's <c>ThemeDictionaries</c> and top-level merged includes.</summary>
public static class ThemeVariantMapParser
{
    private static readonly XNamespace XamlNamespace = "http://schemas.microsoft.com/winfx/2006/xaml";
    private static readonly string[] IncludeElements = ["ResourceInclude", "MergeResourceInclude"];

    /// <exception cref="InvalidDataException">The entry file is not well-formed XML.</exception>
    public static ThemeVariantMap Parse(string entryFile, string baseDirectory, string? assemblyName = null)
    {
        string root = Path.GetFullPath(baseDirectory);
        string entry = Path.GetFullPath(entryFile);

        XDocument document;
        try
        {
            document = XDocument.Load(entry);
        }
        catch (XmlException ex)
        {
            throw new InvalidDataException($"{entry}: {ex.Message}", ex);
        }

        List<UnresolvedInclude> unresolved = [];
        List<ThemeVariant> variants = [];

        foreach (XElement themeDictionaries in document.Descendants().Where(e => e.Name.LocalName.EndsWith("ThemeDictionaries", StringComparison.Ordinal)))
        {
            foreach (XElement child in themeDictionaries.Elements())
            {
                XAttribute? key = child.Attribute(XamlNamespace + "Key");
                if (key is null)
                {
                    continue;
                }

                if (IncludeElements.Contains(child.Name.LocalName, StringComparer.Ordinal))
                {
                    List<string> roots = [];
                    ResolveInto(child.Attribute("Source")?.Value, entry, root, assemblyName, roots, unresolved);
                    variants.Add(new ThemeVariant(key.Value, roots, Inline: false));
                }
                else
                {
                    // Inline <ResourceDictionary x:Key="V">: its keys carry the variant themselves.
                    variants.Add(new ThemeVariant(key.Value, [], Inline: true));
                }
            }
        }

        List<string> sharedRoots = [entry];
        foreach (XElement merged in document.Descendants().Where(e => e.Name.LocalName.EndsWith("MergedDictionaries", StringComparison.Ordinal)))
        {
            if (IsUnderThemeDictionaries(merged))
            {
                continue;
            }

            foreach (XElement child in merged.Elements().Where(e => IncludeElements.Contains(e.Name.LocalName, StringComparer.Ordinal)))
            {
                ResolveInto(child.Attribute("Source")?.Value, entry, root, assemblyName, sharedRoots, unresolved);
            }
        }

        return new ThemeVariantMap(Dedup(sharedRoots), variants, unresolved);
    }

    private static void ResolveInto(string? source, string includingFile, string root, string? assemblyName,
                                    List<string> into, List<UnresolvedInclude> unresolved)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            return;
        }

        if (ResourceIncludeResolver.TryResolveSource(source.Trim(), includingFile, root, assemblyName, out string resolved, out string reason))
        {
            into.Add(resolved);
        }
        else
        {
            unresolved.Add(new UnresolvedInclude(includingFile, source.Trim(), reason));
        }
    }

    private static bool IsUnderThemeDictionaries(XElement element)
    {
        return element.Ancestors().Any(a => a.Name.LocalName.EndsWith("ThemeDictionaries", StringComparison.Ordinal));
    }

    private static IReadOnlyList<string> Dedup(IEnumerable<string> files)
    {
        HashSet<string> seen = new(StringComparer.Ordinal);
        List<string> ordered = [];
        foreach (string file in files)
        {
            if (seen.Add(file))
            {
                ordered.Add(file);
            }
        }

        return ordered;
    }
}
