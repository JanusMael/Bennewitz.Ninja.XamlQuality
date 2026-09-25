namespace Bennewitz.Ninja.XamlQuality.ThemeAudit;

/// <summary>The AXAML/XAML files under a directory, skipping build output, git, and any excluded directories.</summary>
/// <remarks>
/// ⚠ <b>Ordered by the path below the directory, written with <c>/</c>, so every platform lists the
/// same files in the same order.</b> An ordinal sort of the full path does not: <c>\</c> sorts after
/// digits and capitals where <c>/</c> sorts before them, so on Windows <c>ControlsExtra\</c> came
/// before <c>Controls\</c>, and the digest over the list moved with it.
/// </remarks>
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
            .OrderBy(f => Path.GetRelativePath(directory, f).Replace('\\', '/'), StringComparer.Ordinal)
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
