namespace Bennewitz.Ninja.XamlQuality.ThemeAudit;

/// <summary>The AXAML/XAML files under a directory, in ordinal path order, skipping build output, git, and any excluded directories.</summary>
internal static class XamlFiles
{
    private static readonly string[] SkippedDirectories = ["bin", "obj", ".git"];

    /// <param name="directory">The root to scan.</param>
    /// <param name="excludedDirectories">Absolute directories whose files are left out (a generated subtree the consumer does not author).</param>
    public static IReadOnlyList<string> Enumerate(string directory, IReadOnlyList<string>? excludedDirectories = null)
    {
        string[] excluded = excludedDirectories?.Select(d => Path.TrimEndingDirectorySeparator(Path.GetFullPath(d))).ToArray() ?? [];
        return Directory
            .EnumerateFiles(directory, "*.*xaml", SearchOption.AllDirectories)
            .Where(f => f.EndsWith(".axaml", StringComparison.OrdinalIgnoreCase)
                        || f.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase))
            .Where(f => !IsUnderSkippedDirectory(directory, f))
            .Where(f => !excluded.Any(e => IsUnder(e, f)))
            .Order(StringComparer.Ordinal)
            .ToList();
    }

    private static bool IsUnder(string directory, string file)
    {
        string full = Path.GetFullPath(file);
        return full.StartsWith(directory + Path.DirectorySeparatorChar, StringComparison.Ordinal)
               || full.StartsWith(directory + Path.AltDirectorySeparatorChar, StringComparison.Ordinal);
    }

    private static bool IsUnderSkippedDirectory(string root, string file)
    {
        string relative = Path.GetRelativePath(root, file);
        string[] segments = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return segments.Take(segments.Length - 1)
                       .Any(segment => SkippedDirectories.Contains(segment, StringComparer.OrdinalIgnoreCase));
    }
}
