using System.Text.Json;

namespace XamlQuality.Tests.Packaging;

/// <summary>
/// Guards the Claude Code plugin this repository serves: the <c>drivable-ui</c> skill, which
/// <c>.claude-plugin/marketplace.json</c> lists and <c>docs/ai-drivable-ui/plugin/</c> holds.
/// </summary>
/// <remarks>
/// <para>
/// ⛔ <b>A broken marketplace breaks every later install and update.</b> A machine that added this
/// repository with <c>claude plugin marketplace add</c> reads the manifest from <c>main</c>, so a
/// moved folder or a renamed plugin fails for every install and update after it merges. <c>claude
/// plugin validate</c> sees most of it, but CI does not run it; these tests hold what breaks an
/// install.
/// </para>
/// <para>
/// ⚠ <b>No <c>version</c>, on purpose.</b> A pinned version keeps every install on its cached copy
/// until the string changes, so a skill edited without a bump would drift from the guide it
/// condenses. With none, Claude Code versions a relative-path plugin by its commit, and an update
/// picks up every change. <c>claude plugin validate</c> warns about the missing version, and that
/// warning is expected.
/// </para>
/// </remarks>
public sealed class PluginMarketplaceTests
{
    private const string PluginName = "drivable-ui";

    [Fact]
    public void The_marketplace_lists_the_plugin_by_a_path_that_holds_its_manifest()
    {
        string manifestPath = Path.Combine(PluginRoot(MarketplaceEntry()), ".claude-plugin", "plugin.json");
        Assert.True(File.Exists(manifestPath), $"{manifestPath} is missing, so the plugin installs without its metadata.");

        // The entry's name is what a user installs and enables; the manifest's is what the skill is
        // namespaced under. Two different names make one plugin answer to both.
        using JsonDocument manifest = JsonDocument.Parse(File.ReadAllText(manifestPath));
        Assert.Equal(PluginName, manifest.RootElement.GetProperty("name").GetString());
    }

    [Fact]
    public void The_skill_is_where_the_plugin_loads_skills_and_is_named_for_its_folder()
    {
        string skill = Path.Combine(PluginRoot(MarketplaceEntry()), "skills", PluginName, "SKILL.md");

        Assert.True(File.Exists(skill), $"{skill} is missing, so the plugin installs with no skill in it.");
        Assert.Equal(PluginName, FrontmatterName(skill));
    }

    [Fact]
    public void No_version_is_pinned_so_an_update_picks_up_every_change()
    {
        JsonElement entry = MarketplaceEntry();
        using JsonDocument manifest =
            JsonDocument.Parse(File.ReadAllText(Path.Combine(PluginRoot(entry), ".claude-plugin", "plugin.json")));

        Assert.False(entry.TryGetProperty("version", out _), Pinned("The marketplace entry"));
        Assert.False(manifest.RootElement.TryGetProperty("version", out _), Pinned("plugin.json"));

        static string Pinned(string where) =>
            where + " pins a version. A pinned version keeps every install on its cached copy until the "
            + "string changes, so the skill would drift from the guide it condenses. Leave version out of "
            + "both, and Claude Code versions the plugin by its commit.";
    }

    [Fact]
    public void The_skill_sends_its_reader_to_the_guide_that_exists()
    {
        string skill = File.ReadAllText(Path.Combine(PluginRoot(MarketplaceEntry()), "skills", PluginName, "SKILL.md"));

        Assert.True(File.Exists(Path.Combine(RepoRoot(), "docs", "ai-drivable-ui.md")),
            "docs/ai-drivable-ui.md, the guide the skill condenses, is missing.");
        Assert.Contains("`docs/ai-drivable-ui.md`", skill, StringComparison.Ordinal);
        Assert.Contains(
            "https://raw.githubusercontent.com/JanusMael/Bennewitz.Ninja.XamlQuality/main/docs/ai-drivable-ui.md",
            skill,
            StringComparison.Ordinal);
    }

    [Fact]
    public void The_guide_gives_the_commands_the_marketplace_answers_to()
    {
        string guide = File.ReadAllText(Path.Combine(RepoRoot(), "docs", "ai-drivable-ui.md"));

        using JsonDocument marketplace = JsonDocument.Parse(File.ReadAllText(MarketplacePath()));
        string marketplaceName = marketplace.RootElement.GetProperty("name").GetString()!;

        Assert.Contains("claude plugin marketplace add JanusMael/Bennewitz.Ninja.XamlQuality", guide, StringComparison.Ordinal);
        Assert.Contains($"claude plugin install {PluginName}@{marketplaceName}", guide, StringComparison.Ordinal);
        Assert.Contains($"claude plugin marketplace update {marketplaceName}", guide, StringComparison.Ordinal);
        Assert.Contains($"claude plugin update {PluginName}@{marketplaceName}", guide, StringComparison.Ordinal);
    }

    private static string MarketplacePath() => Path.Combine(RepoRoot(), ".claude-plugin", "marketplace.json");

    /// <summary>The marketplace's entry for the plugin, which must exist.</summary>
    private static JsonElement MarketplaceEntry()
    {
        string path = MarketplacePath();
        Assert.True(File.Exists(path), $"{path} is missing, so nothing can add this repository as a marketplace.");

        using JsonDocument marketplace = JsonDocument.Parse(File.ReadAllText(path));
        JsonElement[] entries =
        [
            .. marketplace.RootElement.GetProperty("plugins").EnumerateArray()
                .Where(entry => entry.GetProperty("name").GetString() == PluginName)
                .Select(entry => entry.Clone()),
        ];

        Assert.True(entries.Length == 1, $"The marketplace lists {entries.Length} plugins named {PluginName}; it must list one.");
        return entries[0];
    }

    /// <summary>The directory the entry's relative <c>source</c> names, which must exist inside the repository.</summary>
    private static string PluginRoot(JsonElement entry)
    {
        string source = entry.GetProperty("source").GetString()!;

        Assert.StartsWith("./", source, StringComparison.Ordinal);
        Assert.DoesNotContain("..", source, StringComparison.Ordinal);

        string root = Path.GetFullPath(Path.Combine(RepoRoot(), source[2..]));
        Assert.True(Directory.Exists(root), $"The marketplace's source {source} names no directory.");
        return root;
    }

    /// <summary>The <c>name</c> in a skill's frontmatter, the block between its first two <c>---</c> lines.</summary>
    private static string? FrontmatterName(string path)
    {
        string[] lines = [.. File.ReadLines(path).Select(line => line.TrimEnd('\r'))];
        Assert.True(lines.Length > 0 && lines[0] == "---", $"{path} does not open with frontmatter.");

        return lines
            .Skip(1)
            .TakeWhile(line => line != "---")
            .Where(line => line.StartsWith("name:", StringComparison.Ordinal))
            .Select(line => line["name:".Length..].Trim())
            .FirstOrDefault();
    }

    /// <summary>
    /// Walks up from the test assembly to the directory holding the solution, so the tests do not
    /// depend on the working directory a runner happens to choose.
    /// </summary>
    private static string RepoRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "XamlQuality.slnx")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return directory.FullName;
    }
}
