using Bennewitz.Ninja.XamlQuality;
using Bennewitz.Ninja.XamlQuality.Rules;
using XamlQuality.Tests.Rules.PeerFakes;

namespace XamlQuality.Tests.Rules;

/// <summary>
/// BNXQ1006 over <c>PeerFakes.cs</c>: a themed control is reported when nothing in its chain
/// overrides peer creation, and passes when anything does.
/// </summary>
public sealed class CustomControlPeerRuleTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("xq-").FullName;

    public void Dispose()
    {
        if (Directory.Exists(_root)) { Directory.Delete(_root, recursive: true); }
    }

    private void Write(string name, string markup) =>
        File.WriteAllText(Path.Combine(_root, name), markup);

    private XamlRuleResult Analyze() =>
        new CustomControlPeerRule().Analyze(XamlScanContext.Load(_root).WithAssemblies(typeof(PeerlessBadge).Assembly));

    private XamlRuleResult AnalyzeWithoutAssemblies() =>
        new CustomControlPeerRule().Analyze(XamlScanContext.Load(_root));

    /// <summary>A resource dictionary holding one <c>ControlTheme</c> per target, one to a line.</summary>
    private static string Themes(params string[] targets) =>
        "<ResourceDictionary xmlns=\"https://github.com/avaloniaui\"\n"
        + "                    xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\"\n"
        + "                    xmlns:t=\"using:XamlQuality.Tests.Rules.PeerFakes\">\n"
        + string.Concat(targets.Select((target, index) =>
            $"  <ControlTheme x:Key=\"Theme{index}\" TargetType=\"{target}\"><Setter Property=\"Template\"><ControlTemplate><Border /></ControlTemplate></Setter></ControlTheme>\n"))
        + "</ResourceDictionary>";

    /// <summary>⛔ The case the rule exists for: a themed control that keeps the framework's empty peer.</summary>
    [Fact]
    public void APeerlessThemedControl_IsReported()
    {
        Write("Badge.axaml", Themes("t:PeerlessBadge"));

        XamlRuleResult result = Analyze();

        XamlFinding finding = Assert.Single(result.Findings);
        Assert.Equal("BNXQ1006", finding.RuleId);
        Assert.Equal(4, finding.Line);
        Assert.Contains("PeerlessBadge derives from FakeTemplatedControl, and nothing between it and FakePeerControl overrides OnCreateAutomationPeer", finding.Message, StringComparison.Ordinal);
        Assert.Equal(1, result.Inspected);
        Assert.Empty(result.Skipped);
    }

    /// <summary>⚠ A content base keeps the empty peer too, as Avalonia's <c>ContentControl</c> does.</summary>
    [Fact]
    public void APeerlessControlOnAContentBase_IsReported()
    {
        Write("Pane.axaml", Themes("t:PeerlessPane"));

        XamlFinding finding = Assert.Single(Analyze().Findings);
        Assert.Contains("PeerlessPane derives from FakeContentControl", finding.Message, StringComparison.Ordinal);
    }

    /// <summary>The fix: a control that overrides peer creation has a peer.</summary>
    [Fact]
    public void AControlWithAPeerOfItsOwn_IsClean()
    {
        Write("Badge.axaml", Themes("t:PeeredBadge"));

        XamlRuleResult result = Analyze();

        Assert.Empty(result.Findings);
        Assert.Equal(1, result.Inspected);
        Assert.Empty(result.Skipped);
    }

    /// <summary>An override anywhere below the root counts, including one in the consumer's own base.</summary>
    [Fact]
    public void AControlDerivedFromOneWithAPeer_IsClean()
    {
        Write("Variant.axaml", Themes("t:PeeredBadgeVariant"));

        XamlRuleResult result = Analyze();

        Assert.Empty(result.Findings);
        Assert.Equal(1, result.Inspected);
    }

    /// <summary>A framework base that gives a peer gives it to the control, as Avalonia's <c>Button</c> does.</summary>
    [Fact]
    public void AControlOnAFrameworkBaseWithAPeer_IsClean()
    {
        Write("Toolbar.axaml", Themes("t:ToolbarButton"));

        XamlRuleResult result = Analyze();

        Assert.Empty(result.Findings);
        Assert.Equal(1, result.Inspected);
    }

    /// <summary>
    /// ⭐ The opt-out: a decorative control that returns the empty peer in its own code has decided,
    /// and says so where the control is written.
    /// </summary>
    [Fact]
    public void ADecorativeControlThatSaysSoInItsCode_IsClean()
    {
        Write("Rule.axaml", Themes("t:DecorativeRule"));

        XamlRuleResult result = Analyze();

        Assert.Empty(result.Findings);
        Assert.Equal(1, result.Inspected);
    }

    /// <summary>⚠ Both spellings of a theme's target are read.</summary>
    [Fact]
    public void TheXTypeSpelling_IsUnderstood()
    {
        Write("Badge.axaml", Themes("{x:Type t:PeerlessBadge}"));

        XamlRuleResult result = Analyze();

        Assert.Single(result.Findings);
        Assert.Equal(1, result.Inspected);
    }

    /// <summary>⚠ A control themed twice is one control, reported once, at its first theme.</summary>
    [Fact]
    public void AControlThemedTwice_IsReportedOnce()
    {
        Write("A.axaml", Themes("t:PeerlessBadge"));
        Write("B.axaml", Themes("t:PeerlessBadge"));

        XamlRuleResult result = Analyze();

        XamlFinding finding = Assert.Single(result.Findings);
        Assert.Equal("A.axaml", finding.RelativePath);
        Assert.Equal(1, result.Inspected);
    }

    /// <summary>A theme for a type the scan was not given is a framework's or a package's, not the scan's to explain.</summary>
    [Fact]
    public void AThemeForATypeTheScanWasNotGiven_IsNeitherCheckedNorNamed()
    {
        Write("Other.axaml", Themes("SplitButton", "t:SomethingElse"));

        XamlRuleResult result = Analyze();

        Assert.Empty(result.Findings);
        Assert.Equal(0, result.Inspected);
        Assert.Empty(result.Skipped);
    }

    /// <summary>Markup with no <c>ControlTheme</c> gives the rule nothing to look at, and it says so.</summary>
    [Fact]
    public void MarkupWithNoControlTheme_InspectsNothing()
    {
        Write("View.axaml", "<UserControl xmlns=\"https://github.com/avaloniaui\"><Button Content=\"Go\" /></UserControl>");

        XamlRuleResult result = Analyze();

        Assert.Empty(result.Findings);
        Assert.Equal(0, result.Inspected);
        Assert.Empty(result.Skipped);
    }

    /// <summary>
    /// ⛔ A scan given no assemblies checks nothing, and names every themed control, so it cannot pass
    /// for a clean one.
    /// </summary>
    [Fact]
    public void WithoutAssemblies_EveryThemedControlIsNamedAsSkipped()
    {
        Write("Badges.axaml", Themes("t:PeerlessBadge", "t:PeeredBadge"));

        XamlRuleResult result = AnalyzeWithoutAssemblies();

        Assert.Empty(result.Findings);
        Assert.Equal(0, result.Inspected);
        Assert.Equal(["PeerlessBadge", "PeeredBadge"], result.Skipped.Select(skip => skip.Subject));
        Assert.All(result.Skipped, skip => Assert.Contains("given no assemblies", skip.Reason, StringComparison.Ordinal));
    }

    /// <summary>⚠ A type with no peer creation to read is named, not guessed at.</summary>
    [Fact]
    public void ATypeWithNoPeerMethod_IsSkippedNotReported()
    {
        Write("Plain.axaml", Themes("t:NoPeerMethod"));

        XamlRuleResult result = Analyze();

        Assert.Empty(result.Findings);
        Assert.Equal(0, result.Inspected);
        XamlSkip skip = Assert.Single(result.Skipped);
        Assert.Equal("NoPeerMethod", skip.Subject);
        Assert.Contains("Nothing in its chain declares OnCreateAutomationPeer", skip.Reason, StringComparison.Ordinal);
    }

    /// <summary>⚠ A name two scanned types carry cannot say which one a theme means, so neither is checked.</summary>
    [Fact]
    public void ANameTwoScannedTypesCarry_IsSkipped()
    {
        Write("Twin.axaml", Themes("t:Twin"));

        XamlRuleResult result = Analyze();

        Assert.Empty(result.Findings);
        Assert.Equal(0, result.Inspected);
        XamlSkip skip = Assert.Single(result.Skipped);
        Assert.Equal("Twin", skip.Subject);
        Assert.Contains("XamlQuality.Tests.Rules.PeerFakes.Twin and XamlQuality.Tests.Rules.PeerFakes.Elsewhere.Twin", skip.Reason, StringComparison.Ordinal);
    }
}
