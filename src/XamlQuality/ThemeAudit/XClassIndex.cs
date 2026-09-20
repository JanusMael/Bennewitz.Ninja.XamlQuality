using System.Xml.Linq;

namespace Bennewitz.Ninja.XamlQuality.ThemeAudit;

/// <summary>
/// Maps a code-behind dictionary type to the AXAML file that declares it, by that file's root
/// <c>x:Class</c>. A theme includes a compiled <c>ResourceDictionary</c> subclass by writing it as
/// an element — Semi's <c>&lt;semi:Light/&gt;</c> is the type <c>Semi.Avalonia.Tokens.Palette.Light</c>,
/// whose XAML is <c>Tokens/Palette/Light.axaml</c> (<c>x:Class="…Light"</c>). The element carries
/// only the simple type name, so the index resolves by that.
/// </summary>
public sealed class XClassIndex
{
    private static readonly XNamespace XamlNamespace = "http://schemas.microsoft.com/winfx/2006/xaml";
    private static readonly string[] SkippedDirectories = ["bin", "obj", ".git"];

    private readonly ILookup<string, string> _bySimpleName;

    private XClassIndex(ILookup<string, string> bySimpleName)
    {
        _bySimpleName = bySimpleName;
    }

    /// <summary>Indexes every <c>x:Class</c>-carrying AXAML/XAML file under <paramref name="baseDirectory"/>.</summary>
    public static XClassIndex Build(string baseDirectory)
    {
        List<(string SimpleName, string File)> entries = [];
        foreach (string file in EnumerateXamlFiles(Path.GetFullPath(baseDirectory)))
        {
            XElement? root;
            try
            {
                root = XDocument.Load(file).Root;
            }
            catch (System.Xml.XmlException)
            {
                continue; // a file that will not parse cannot be a resolution target
            }

            string? className = root?.Attribute(XamlNamespace + "Class")?.Value;
            if (!string.IsNullOrWhiteSpace(className))
            {
                int dot = className.LastIndexOf('.');
                entries.Add((dot >= 0 ? className[(dot + 1)..] : className, file));
            }
        }

        return new XClassIndex(entries.ToLookup(e => e.SimpleName, e => e.File, StringComparer.Ordinal));
    }

    /// <summary>
    /// Resolves a simple type name to its file. Fails when no file declares that <c>x:Class</c>
    /// (a pure-code dictionary with no XAML, such as Semi's icon set) or when more than one does.
    /// </summary>
    public bool TryResolve(string simpleTypeName, out string file, out string reason)
    {
        List<string> matches = _bySimpleName[simpleTypeName].ToList();
        if (matches.Count == 1)
        {
            file = matches[0];
            reason = string.Empty;
            return true;
        }

        file = string.Empty;
        reason = matches.Count == 0
            ? $"no x:Class type named {simpleTypeName}"
            : $"ambiguous x:Class type {simpleTypeName} ({matches.Count} files)";
        return false;
    }

    private static IEnumerable<string> EnumerateXamlFiles(string directory)
    {
        return Directory
            .EnumerateFiles(directory, "*.*xaml", SearchOption.AllDirectories)
            .Where(f => f.EndsWith(".axaml", StringComparison.OrdinalIgnoreCase)
                        || f.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase))
            .Where(f => !IsUnderSkippedDirectory(directory, f))
            .Order(StringComparer.Ordinal);
    }

    private static bool IsUnderSkippedDirectory(string root, string file)
    {
        string relative = Path.GetRelativePath(root, file);
        string[] segments = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return segments.Take(segments.Length - 1)
                       .Any(segment => SkippedDirectories.Contains(segment, StringComparer.OrdinalIgnoreCase));
    }
}
