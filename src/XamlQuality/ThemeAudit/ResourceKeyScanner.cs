using System.Xml;
using System.Xml.Linq;

namespace Bennewitz.Ninja.XamlQuality.ThemeAudit;

/// <summary>
/// Enumerates the resource keys (<c>x:Key</c>) defined by the AXAML and XAML files under a
/// directory. The XML is parsed, not pattern-matched, so a key split across lines or written
/// with unusual spacing is still found; a file that is not well-formed XML is reported as an
/// error rather than silently undercounted.
/// </summary>
internal static class ResourceKeyScanner
{
    private static readonly XNamespace XamlNamespace = "http://schemas.microsoft.com/winfx/2006/xaml";

    private static readonly string[] SkippedDirectories = ["bin", "obj", ".git"];

    /// <summary>
    /// Scans every <c>.axaml</c> and <c>.xaml</c> file under <paramref name="directory"/>,
    /// in ordinal path order, and returns one entry per <c>x:Key</c> found.
    /// </summary>
    /// <exception cref="InvalidDataException">A file is not well-formed XML.</exception>
    public static IReadOnlyList<ResourceKey> Scan(string directory)
    {
        List<ResourceKey> keys = [];

        foreach (string file in EnumerateXamlFiles(directory))
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
                XAttribute? key = element.Attribute(XamlNamespace + "Key");
                if (key is null)
                {
                    continue;
                }

                int line = ((IXmlLineInfo)element).HasLineInfo() ? ((IXmlLineInfo)element).LineNumber : 0;
                keys.Add(new ResourceKey(file, key.Value, element.Name.LocalName, line));
            }
        }

        return keys;
    }

    private static IEnumerable<string> EnumerateXamlFiles(string directory)
    {
        return Directory
            .EnumerateFiles(directory, "*.*xaml", SearchOption.AllDirectories)
            .Where(IsXamlFile)
            .Where(file => !IsUnderSkippedDirectory(directory, file))
            .Order(StringComparer.Ordinal);
    }

    private static bool IsXamlFile(string file)
    {
        return file.EndsWith(".axaml", StringComparison.OrdinalIgnoreCase)
               || file.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsUnderSkippedDirectory(string root, string file)
    {
        string relative = Path.GetRelativePath(root, file);
        string[] segments = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return segments.Take(segments.Length - 1)
                       .Any(segment => SkippedDirectories.Contains(segment, StringComparer.OrdinalIgnoreCase));
    }
}

/// <summary>One <c>x:Key</c> definition: where it is, what it is called, what element carries it.</summary>
internal sealed record ResourceKey(string File, string Key, string ElementName, int Line);
