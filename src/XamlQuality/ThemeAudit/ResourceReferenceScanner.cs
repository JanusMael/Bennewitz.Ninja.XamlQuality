using System.Xml;
using System.Xml.Linq;

namespace Bennewitz.Ninja.XamlQuality.ThemeAudit;

/// <summary>How a resource key is reached.</summary>
public enum ReferenceKind
{
    /// <summary><c>{DynamicResource Key}</c>: resolved late, per active theme. A key the active
    /// theme does not define resolves to nothing and paints an invisible control — no exception,
    /// the case the audit exists to catch.</summary>
    Dynamic,

    /// <summary><c>{StaticResource Key}</c>: resolved once at load. A missing key throws at load
    /// instead of going invisible, so it is a louder but still real gap.</summary>
    Static,
}

/// <summary>One resource reference: which key, reached how, and where.</summary>
public sealed record ResourceReference(string File, string Key, ReferenceKind Kind, int Line);

/// <summary>
/// Enumerates the resource keys referenced by the AXAML/XAML under a directory, through the
/// markup-extension forms <c>{DynamicResource Key}</c> / <c>{StaticResource Key}</c> (including
/// <c>ResourceKey=</c> and a nested <c>{x:Type ...}</c> key such as
/// <c>BasedOn="{StaticResource {x:Type ToggleButton}}"</c>) and the element forms
/// <c>&lt;DynamicResource ResourceKey="Key"/&gt;</c>. The XML is parsed, not line-matched, so a
/// reference split across lines is still found.
/// </summary>
public static class ResourceReferenceScanner
{
    /// <param name="directory">The root to scan.</param>
    /// <param name="excludedDirectories">Absolute directories under the root to leave out.</param>
    /// <exception cref="InvalidDataException">A file is not well-formed XML.</exception>
    public static IReadOnlyList<ResourceReference> Scan(string directory, IReadOnlyList<string>? excludedDirectories = null)
    {
        List<ResourceReference> references = [];

        foreach (string file in XamlFiles.Enumerate(directory, excludedDirectories))
        {
            XDocument document;
            try
            {
                document = XDocument.Load(file, LoadOptions.SetLineInfo);
            }
            catch (XmlException ex)
            {
                throw new InvalidDataException($"{file}: {ex.Message}", ex);
            }

            foreach (XElement element in document.Descendants())
            {
                int line = ((IXmlLineInfo)element).HasLineInfo() ? ((IXmlLineInfo)element).LineNumber : 0;

                // Element form: <DynamicResource ResourceKey="X"/> / <StaticResource ResourceKey="X"/>.
                if (TryElementKind(element.Name.LocalName, out ReferenceKind elementKind))
                {
                    string? resourceKey = element.Attribute("ResourceKey")?.Value;
                    if (!string.IsNullOrWhiteSpace(resourceKey))
                    {
                        references.Add(new ResourceReference(file, resourceKey.Trim(), elementKind, line));
                    }
                }

                // Markup-extension form in any attribute value.
                foreach (XAttribute attribute in element.Attributes())
                {
                    foreach ((ReferenceKind kind, string key) in ExtractMarkupReferences(attribute.Value))
                    {
                        references.Add(new ResourceReference(file, key, kind, line));
                    }
                }
            }
        }

        return references;
    }

    private static bool TryElementKind(string localName, out ReferenceKind kind)
    {
        switch (localName)
        {
            case "DynamicResource":
                kind = ReferenceKind.Dynamic;
                return true;
            case "StaticResource":
                kind = ReferenceKind.Static;
                return true;
            default:
                kind = default;
                return false;
        }
    }

    /// <summary>
    /// Pulls every <c>{DynamicResource ...}</c> / <c>{StaticResource ...}</c> out of one attribute
    /// value, honouring nested braces so a <c>{x:Type ...}</c> key is captured whole.
    /// </summary>
    internal static IEnumerable<(ReferenceKind Kind, string Key)> ExtractMarkupReferences(string value)
    {
        int i = 0;
        while (i < value.Length)
        {
            int open = value.IndexOf('{', i);
            if (open < 0)
            {
                yield break;
            }

            int close = MatchingBrace(value, open);
            if (close < 0)
            {
                yield break; // unbalanced; nothing more to trust
            }

            string inner = value[(open + 1)..close];
            if (TryReadResourceExtension(inner, out ReferenceKind kind, out string key))
            {
                yield return (kind, key);
            }

            // Recurse into the inside so a key that is itself a resource extension is not missed,
            // but skip past this extension's own opening token first.
            i = open + 1;
        }
    }

    private static bool TryReadResourceExtension(string inner, out ReferenceKind kind, out string key)
    {
        kind = default;
        key = string.Empty;

        string trimmed = inner.TrimStart();
        string name;
        if (trimmed.StartsWith("DynamicResource", StringComparison.Ordinal))
        {
            kind = ReferenceKind.Dynamic;
            name = "DynamicResource";
        }
        else if (trimmed.StartsWith("StaticResource", StringComparison.Ordinal))
        {
            kind = ReferenceKind.Static;
            name = "StaticResource";
        }
        else
        {
            return false;
        }

        string rest = trimmed[name.Length..];
        if (rest.Length > 0 && !char.IsWhiteSpace(rest[0]) && rest[0] != '{')
        {
            return false; // e.g. "DynamicResourceExtension" or "StaticResourceFoo"
        }

        rest = rest.Trim();
        if (rest.StartsWith("ResourceKey", StringComparison.Ordinal))
        {
            int eq = rest.IndexOf('=', StringComparison.Ordinal);
            if (eq < 0)
            {
                return false;
            }

            rest = rest[(eq + 1)..].Trim();
        }

        // Drop any trailing named parameters (rare): the key is up to the first top-level comma.
        rest = TakeUpToTopLevelComma(rest).Trim().Trim('\'', '"');
        if (rest.Length == 0)
        {
            return false;
        }

        key = rest;
        return true;
    }

    private static string TakeUpToTopLevelComma(string text)
    {
        int depth = 0;
        for (int i = 0; i < text.Length; i++)
        {
            switch (text[i])
            {
                case '{':
                    depth++;
                    break;
                case '}':
                    depth--;
                    break;
                case ',' when depth == 0:
                    return text[..i];
            }
        }

        return text;
    }

    private static int MatchingBrace(string text, int open)
    {
        int depth = 0;
        for (int i = open; i < text.Length; i++)
        {
            if (text[i] == '{')
            {
                depth++;
            }
            else if (text[i] == '}')
            {
                depth--;
                if (depth == 0)
                {
                    return i;
                }
            }
        }

        return -1;
    }
}
