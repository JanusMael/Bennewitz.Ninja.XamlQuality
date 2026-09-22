using Bennewitz.Ninja.XamlQuality;

namespace XamlQuality.Tests;

/// <summary>
/// The README's rules table is rendered from the rule types, not maintained by hand.
/// </summary>
/// <remarks>
/// <para>
/// ⭐ <b>Generated rather than merely checked, because a check still needs someone to write the
/// row.</b> Nothing in this library holds <see cref="IXamlRule"/> in a collection — there is no
/// registry, and a consumer names each rule directly — so that table is the only enumeration of
/// the rules that exists anywhere. A rule missing from it ships invisible. That is not
/// hypothetical: <c>XQ1002</c> was added and the table was not, and nothing failed.
/// </para>
/// <para>
/// ⚠ <b>Only the marked region is generated.</b> Whether two rules overlap is a fact about the
/// PAIR, not about either type, so no amount of reflection can emit it — <c>XQ1002</c> excludes
/// <c>Expander</c> because <c>XQ1001</c> already covers it. That prose is hand-written and lives
/// OUTSIDE the markers; a generator that owned the whole section would delete it.
/// </para>
/// <para>
/// ⛔ <b>A rule this cannot render fails the test rather than being skipped.</b> Skipping drops it
/// from the table silently, which is the exact defect this exists to prevent — the same reasoning
/// as <see cref="XamlRuleResult.Inspected"/>.
/// </para>
/// </remarks>
public sealed class RulesCatalogTests
{
    private const string BeginMarker = "<!-- BEGIN GENERATED RULES -->";
    private const string EndMarker = "<!-- END GENERATED RULES -->";

    /// <summary>Set to any non-empty value to rewrite the README region instead of asserting.</summary>
    /// <remarks>
    /// ⓘ Regenerate with <c>XQ_UPDATE_DOCS=1 dotnet test --solution XamlQuality.slnx</c>, then
    /// commit the README. The build deliberately does NOT write it: a build that edits a tracked
    /// file leaves CI with a dirty tree and no way to tell an intended change from a stale one.
    /// </remarks>
    private const string UpdateVariable = "XQ_UPDATE_DOCS";

    [Fact]
    public void TheReadmeRulesTable_IsWhatTheRuleTypesSay()
    {
        string path = Path.Combine(RepositoryRoot(), "README.md");
        string readme = File.ReadAllText(path);
        string rendered = RenderTable();

        if (Environment.GetEnvironmentVariable(UpdateVariable) is { Length: > 0 })
        {
            File.WriteAllText(path, ReplaceRegion(readme, rendered));
            return;
        }

        Assert.Equal(rendered, ExtractRegion(readme), ignoreLineEndingDifferences: true);
    }

    /// <summary>
    /// ⚠ Guards the discovery itself. Every assertion above still passes if reflection stops
    /// finding rules and the region is emptied to match — a table that documents nothing, agreeing
    /// perfectly with a README that says nothing.
    /// </summary>
    [Fact]
    public void RuleDiscovery_FindsTheRulesThatShip()
    {
        IReadOnlyList<IXamlRule> rules = DiscoverRules();

        Assert.Contains(rules, rule => rule.Id == "XQ1001");
        Assert.Contains(rules, rule => rule.Id == "XQ1002");
        Assert.Contains(rules, rule => rule.Id == "XQ1003");
    }

    /// <summary>
    /// ⛔ Two rules sharing an <see cref="IXamlRule.Id"/> would render two rows a reader cannot
    /// tell apart, and would break any suppression keyed on that id.
    /// </summary>
    [Fact]
    public void EveryRuleId_IsUnique()
    {
        string[] ids = [.. DiscoverRules().Select(rule => rule.Id)];

        Assert.Equal(ids.Length, ids.Distinct(StringComparer.Ordinal).Count());
    }

    /// <summary>
    /// ⛔ The table lists what a consumer can actually construct, so an <see cref="IXamlRule"/>
    /// that is not public would vanish from it. Failing here turns "accidentally internal" into a
    /// broken build rather than a rule nobody can find.
    /// </summary>
    [Fact]
    public void EveryRuleType_IsPublic()
    {
        IEnumerable<Type> hidden = ConcreteRuleTypes().Where(type => !type.IsPublic);

        Assert.Empty(hidden.Select(type => type.FullName));
    }

    /// <summary>Renders the region body: the header, the separator, and one row per rule.</summary>
    private static string RenderTable()
    {
        IEnumerable<string> rows = DiscoverRules()
            .OrderBy(rule => rule.Id, StringComparer.Ordinal)
            .Select(rule => "| `" + rule.Id + "` | " + Escape(rule.Summary) + " |");

        return string.Join("\n", ["| Id | Requires |", "|---|---|", .. rows]);
    }

    /// <summary>A pipe in a summary would end the cell early and shift every column after it.</summary>
    private static string Escape(string summary) => summary.Replace("|", @"\|", StringComparison.Ordinal);

    private static IReadOnlyList<IXamlRule> DiscoverRules()
    {
        List<IXamlRule> rules = [];

        foreach (Type type in ConcreteRuleTypes())
        {
            object? instance;
            try
            {
                instance = Activator.CreateInstance(type);
            }
            catch (MissingMethodException)
            {
                // ⛔ Loudly, not silently. A rule needing constructor arguments cannot be rendered
                // from its type alone, and dropping it is how the table goes quietly stale.
                throw new InvalidOperationException(
                    $"{type.FullName} implements {nameof(IXamlRule)} but has no parameterless "
                    + "constructor, so the README table cannot be generated from it. Give it one, "
                    + "or move its Id and Summary somewhere reflection can read without "
                    + "constructing the rule.");
            }

            rules.Add((IXamlRule)instance!);
        }

        return rules;
    }

    private static IEnumerable<Type> ConcreteRuleTypes() =>
        typeof(IXamlRule).Assembly
            .GetTypes()
            .Where(type => typeof(IXamlRule).IsAssignableFrom(type))
            .Where(type => type is { IsAbstract: false, IsInterface: false })
            .OrderBy(type => type.FullName, StringComparer.Ordinal);

    private static string ExtractRegion(string readme)
    {
        (int start, int end) = RegionBounds(readme);

        return readme[start..end].Trim('\r', '\n');
    }

    private static string ReplaceRegion(string readme, string rendered)
    {
        (int start, int end) = RegionBounds(readme);

        return readme[..start] + "\n" + rendered + "\n" + readme[end..];
    }

    /// <summary>The span BETWEEN the markers, exclusive of both.</summary>
    private static (int Start, int End) RegionBounds(string readme)
    {
        int begin = readme.IndexOf(BeginMarker, StringComparison.Ordinal);
        int end = readme.IndexOf(EndMarker, StringComparison.Ordinal);

        Assert.True(begin >= 0, $"README.md has no {BeginMarker} marker.");
        Assert.True(end > begin, $"README.md has no {EndMarker} marker after the opening one.");

        return (begin + BeginMarker.Length, end);
    }

    /// <summary>
    /// Walks up from the test binaries to the folder holding the solution.
    /// </summary>
    /// <remarks>
    /// ⚠ The binaries sit several levels under the repository root and the depth differs by
    /// configuration and target framework, so the root is found by looking for a file only it has
    /// rather than by counting <c>..</c> segments.
    /// </remarks>
    private static string RepositoryRoot()
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "XamlQuality.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException(
            $"No XamlQuality.slnx above {AppContext.BaseDirectory}; this test reads the "
            + "repository's README and must run from a source checkout.");
    }
}
