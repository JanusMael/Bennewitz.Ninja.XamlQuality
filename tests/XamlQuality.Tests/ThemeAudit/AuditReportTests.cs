using Bennewitz.Ninja.XamlQuality.ThemeAudit;

namespace XamlQuality.Tests.ThemeAudit;

/// <summary>
/// The configured audit end to end on the committed fixture under <c>Fixtures/audit</c>: a host
/// theme that omits a key under one variant, a consumer that references it (and a key nothing
/// defines), a token that fails its contrast floor, and a compat-style merged theme that closes
/// the gap. The report must name each finding, contain nothing machine-specific, and render
/// identically twice. The same fixture runs from the command line:
/// <c>theme-audit report --config tests/ThemeAudit.Tests/Fixtures/audit/theme-audit.json --output …</c>.
/// </summary>
public sealed class AuditReportTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("theme-rep-").FullName;
    private readonly string _fixture = FixturePaths.Fixture("audit");

    public void Dispose()
    {
        Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public void Runs_the_configuration_and_finds_the_omitted_key_the_missing_key_and_the_failing_token()
    {
        AuditConfig config = AuditConfig.Load(Path.Combine(_fixture, "theme-audit.json"));
        AuditResult result = AuditRunner.Run(config);

        Assert.Equal(["Host", "Host + compat"], result.Themes.Select(t => t.Config.Name).ToArray());
        Assert.Equal(["App", "Host-only"], result.Consumers.Select(c => c.Config.Name).ToArray());

        // App under Host: HostAccent missing under Dark only; NoSuchKey missing everywhere and static; AppOwn is its own.
        ConsumerThemeFindings appHost = result.Findings.Single(f => f.Consumer.Config.Name == "App" && f.Theme.Config.Name == "Host");
        Assert.Equal(
            [("Light", "NoSuchKey", ReferenceKind.Static), ("Dark", "HostAccent", ReferenceKind.Dynamic), ("Dark", "NoSuchKey", ReferenceKind.Static)],
            appHost.Undefined.Select(u => (u.DisplayName, u.Key, u.WorstKind)).ToArray());

        // App under Host + compat: the merged dictionary closes the HostAccent gap.
        ConsumerThemeFindings appCompat = result.Findings.Single(f => f.Consumer.Config.Name == "App" && f.Theme.Config.Name == "Host + compat");
        Assert.DoesNotContain(appCompat.Undefined, u => u.Key == "HostAccent");
        Assert.Equal(2, appCompat.Undefined.Count(u => u.Key == "NoSuchKey"));

        // Contrast: AppOwn on the page passes in Light, fails in Dark; the marker passes; $Missing is unscored.
        ContrastFinding lightOwn = appHost.Contrast.Single(c => c.DisplayName == "Light" && c.Pair.Foreground == "AppOwn" && c.Pair.Background == "$Page");
        Assert.Equal(ContrastStatus.Pass, lightOwn.Status);
        ContrastFinding darkOwn = appHost.Contrast.Single(c => c.DisplayName == "Dark" && c.Pair.Foreground == "AppOwn" && c.Pair.Background == "$Page");
        Assert.Equal(ContrastStatus.Fail, darkOwn.Status);
        Assert.Equal("HostPage", darkOwn.BackgroundKey);
        Assert.All(appHost.Contrast.Where(c => c.Pair.Background == "$Missing"), c => Assert.Equal(ContrastStatus.Unscored, c.Status));

        // The consumer restricted to Host is not audited against the compat theme.
        Assert.Single(result.Findings, f => f.Consumer.Config.Name == "Host-only");

        // The first candidate directory does not exist; the second was used.
        Assert.All(result.Consumers[0].Files, f => Assert.Contains("app", f));
    }

    [Fact]
    public void Renders_a_deterministic_report_with_the_findings_and_no_machine_specific_text()
    {
        AuditConfig config = AuditConfig.Load(Path.Combine(_fixture, "theme-audit.json"));
        AuditResult result = AuditRunner.Run(config);

        string report = MarkdownReport.Render(result);
        Assert.Equal(report, MarkdownReport.Render(AuditRunner.Run(config)));

        Assert.StartsWith("# Fixture audit\n", report);
        Assert.DoesNotContain(_fixture, report);
        Assert.DoesNotContain("\r", report);

        // Themes and variants.
        Assert.Contains("| Host + compat | `host/Host.axaml` + `host/Compat.axaml` |", report);
        Assert.Contains("| Host | Light | Light |", report);
        Assert.Contains("`HostPage` #FF16161A", report);

        // Consumers: candidates shown as configured, not the one that happened to exist.
        Assert.Contains("`missing/elsewhere or app/Themes`", report);

        // Undefined keys: the summary row and the per-key rows.
        Assert.Contains("| App | Host | 2 | 1 | 1 |", report);
        Assert.Contains("### App under Host\n", report);
        Assert.Contains("| `HostAccent` | dynamic (invisible) | Dark |", report);
        Assert.Contains("| `NoSuchKey` | static (throws) | all |", report);
        Assert.Contains("| App | Host + compat | 1 | 1 | 0 |", report);

        // Contrast: the failing ratio in bold, the unscored pair explained.
        Assert.Contains("### App under Host\n\n3 pairs × 2 variants: 3 pass, 1 below the floor, 2 not measurable.", report);
        Assert.Matches(@"\| `AppOwn` \| `\$Page` \| 4\.5 \| \d+\.\d\d \| \*\*\d\.\d\d\*\* \|", report);
        Assert.Contains("names no $Missing surface", report);
    }

    [Fact]
    public void Rejects_an_inconsistent_configuration_and_names_a_missing_consumer_directory()
    {
        AuditConfigException unknownTheme = Assert.Throws<AuditConfigException>(() => AuditConfig.Parse(
            """
            {
              "themes": [ { "name": "Host", "entry": "host/Host.axaml", "baseDirectory": "host" } ],
              "consumers": [ { "name": "App", "paths": ["app"], "themes": ["Nope"] } ]
            }
            """, _root));
        Assert.Contains("unknown theme 'Nope'", unknownTheme.Message);

        AuditConfigException noEntry = Assert.Throws<AuditConfigException>(() => AuditConfig.Parse(
            """{ "themes": [ { "name": "Host" } ] }""", _root));
        Assert.Contains("'entry' and 'baseDirectory'", noEntry.Message);

        AuditConfig missingDirectory = AuditConfig.Parse(
            """
            {
              "themes": [ { "name": "Host", "entry": "host/Host.axaml", "baseDirectory": "host" } ],
              "consumers": [ { "name": "Gone", "paths": [["nowhere/a", "nowhere/b"]] } ]
            }
            """, _fixture);
        DirectoryNotFoundException notFound = Assert.Throws<DirectoryNotFoundException>(() => AuditRunner.Run(missingDirectory));
        Assert.Contains("nowhere/a or nowhere/b", notFound.Message);
    }
}
