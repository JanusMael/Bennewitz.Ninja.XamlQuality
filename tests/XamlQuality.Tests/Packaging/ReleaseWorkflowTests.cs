using Xunit;

namespace XamlQuality.Tests.Packaging;

/// <summary>
/// Guards the release workflow's publishing steps against a glob.
/// </summary>
/// <remarks>
/// <para>
/// ⛔ <b>A published NuGet version can never be replaced.</b> A <c>*.nupkg</c> glob publishes
/// whatever happens to be in the folder, so the one time it picks up something unintended is the
/// one time it cannot be undone — only unlisted.
/// </para>
/// <para>
/// ⚠ <b>The argument this exists to refuse is "it is fine, every packed id is pushable."</b> That
/// is true of today's project list and of no other. A third packable project — an analyzer, a
/// second tool, a sample — would be swept up by a glob with nothing objecting. Bennewitz.Ninja.DiffView
/// came within one project of exactly that: its <c>src/ThemeAudit</c> was packable and
/// local-feed-only, so a glob would have published it.
/// </para>
/// <para>
/// ⭐ <b>The <c>gh release create</c> step is checked as well as the push, and it is the one more
/// likely to rot.</b> Its failure is quieter — a GitHub release asset can be deleted, so a glob
/// there never announces itself the way a permanent nuget.org id does. It just ships something
/// nobody chose.
/// </para>
/// </remarks>
public sealed class ReleaseWorkflowTests
{
    /// <summary>
    /// Every published id is named, and no step globs — anywhere in the file, not just at the push.
    /// </summary>
    [Fact]
    public void The_release_workflow_names_every_package_it_publishes()
    {
        string path = Path.Combine(RepoRoot(), ".github", "workflows", "release.yml");
        Assert.True(File.Exists(path), "There is no release workflow, so nothing publishes — and nothing gates what would.");

        // Comment lines go first: the comments above both publishing steps explain the very glob
        // this test forbids, and a whole-file search would fail on the explanation.
        string instructions = string.Join(
            '\n',
            File.ReadLines(path).Where(line => !line.TrimStart().StartsWith('#')));

        Assert.DoesNotContain("*.nupkg", instructions, StringComparison.Ordinal);
        Assert.Contains("Bennewitz.Ninja.XamlQuality.${{ env.VERSION }}.nupkg", instructions, StringComparison.Ordinal);
        Assert.Contains("Bennewitz.Ninja.XamlQuality.ThemeAudit.${{ env.VERSION }}.nupkg", instructions, StringComparison.Ordinal);
    }

    /// <summary>
    /// Both publishing steps are present. Without this the test above passes vacuously on a file
    /// that has stopped publishing at all.
    /// </summary>
    [Fact]
    public void The_release_workflow_still_has_both_publishing_steps()
    {
        string instructions = File.ReadAllText(Path.Combine(RepoRoot(), ".github", "workflows", "release.yml"));

        Assert.Contains("dotnet nuget push", instructions, StringComparison.Ordinal);
        Assert.Contains("gh release create", instructions, StringComparison.Ordinal);
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
