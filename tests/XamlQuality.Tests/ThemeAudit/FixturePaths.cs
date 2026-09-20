namespace Bennewitz.Ninja.XamlQuality.ThemeAudit.Tests;

/// <summary>Locates the committed fixtures under this test project, and the repository root, from the runtime directory.</summary>
internal static class FixturePaths
{
    // ⛔ THESE TWO CONSTANTS ARE WHY THE ANALYSIS WAS NOT PORTABLE, and they were the only part
    // of the move that needed thought. Everything that came into this repository located itself
    // by walking up until it saw a particular project or solution file — facts about ONE
    // repository, baked into code that is otherwise about XAML.
    // ⭐ They failed loudly here, which is the good case: the walk throws when the marker is
    // missing. The same shape inside a RULE would have found no files and reported zero findings,
    // which is indistinguishable from clean markup.
    private const string ProjectMarker = "XamlQuality.Tests.csproj";
    private const string RepoMarker = "XamlQuality.slnx";

    /// <summary>The directory of the named fixture under <c>ThemeAudit/Fixtures/</c>.</summary>
    public static string Fixture(string name)
    {
        // The fixtures gained a ThemeAudit/ segment when the two test projects merged into one.
        return Path.Combine(FindUp(ProjectMarker), "ThemeAudit", "Fixtures", name);
    }

    /// <summary>The repository root — where <c>theme-audit.json</c> and <c>docs/theme-audit.md</c> live.</summary>
    public static string RepoRoot => FindUp(RepoMarker);

    private static string FindUp(string marker)
    {
        string? directory = AppContext.BaseDirectory;
        for (int i = 0; i < 12 && !string.IsNullOrEmpty(directory); i++)
        {
            if (File.Exists(Path.Combine(directory, marker)))
            {
                return directory;
            }

            directory = Path.GetDirectoryName(directory);
        }

        throw new InvalidOperationException(
            $"Could not find {marker} by walking up from AppContext.BaseDirectory = '{AppContext.BaseDirectory}'.");
    }
}
