using Bennewitz.Ninja.XamlQuality;
using Bennewitz.Ninja.XamlQuality.Rules;
using Xunit;

namespace XamlQuality.Tests.Rules;

/// <summary>
/// <see cref="ExpanderAutomationNameRule"/> against markup written for the test, not against a
/// repository.
/// </summary>
/// <remarks>
/// ⭐ <b>This is the dividend of rules that take a scan root as a value.</b> The guard this rule
/// came from could only run against its own repository's <c>src</c>, so it could assert "no
/// offenders today" and never that it would FIND one. Here both directions are testable.
/// </remarks>
public sealed class ExpanderAutomationNameRuleTests : IDisposable
{
    private readonly string _root;

    public ExpanderAutomationNameRuleTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "xq-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) { Directory.Delete(_root, recursive: true); }
    }

    private void Write(string name, string markup) =>
        File.WriteAllText(Path.Combine(_root, name), markup);

    private XamlRuleResult Run()
        => new ExpanderAutomationNameRule().Analyze(XamlScanContext.Load(_root));

    private const string NamespaceHeader =
        "xmlns=\"https://github.com/avaloniaui\" " +
        "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\"";

    [Fact]
    public void AnUnnamedExpander_IsReported()
    {
        Write("Bad.axaml", $"<UserControl {NamespaceHeader}><Expander Header=\"Proxy\" /></UserControl>");

        XamlRuleResult result = Run();

        Assert.Equal(1, result.Inspected);
        XamlFinding finding = Assert.Single(result.Findings);
        Assert.Equal("XQ1001", finding.RuleId);
        Assert.Contains("Bad.axaml", finding.RelativePath, StringComparison.Ordinal);
    }

    [Fact]
    public void ANamedExpander_IsNotReported()
    {
        Write("Good.axaml",
            $"<UserControl {NamespaceHeader}>"
            + "<Expander Header=\"Proxy\" AutomationProperties.Name=\"Proxy settings\" />"
            + "</UserControl>");

        XamlRuleResult result = Run();

        Assert.Equal(1, result.Inspected);
        Assert.Empty(result.Findings);
    }

    /// <summary>
    /// ⚠ The property-ELEMENT spelling. Checking only the attribute form is how this rule would
    /// report a violation against markup that is actually correct.
    /// </summary>
    [Fact]
    public void AnExpanderNamedByPropertyElement_IsNotReported()
    {
        Write("Element.axaml",
            $"<UserControl {NamespaceHeader}><Expander Header=\"Proxy\">"
            + "<AutomationProperties.Name>Proxy settings</AutomationProperties.Name>"
            + "</Expander></UserControl>");

        Assert.Empty(Run().Findings);
    }

    /// <summary>
    /// ⛔ The same bar for the element spelling: an empty property element is as empty as
    /// <c>Name=""</c>. The helper is shared with XQ1002, so this is also the check that the two rules
    /// have not drifted apart.
    /// </summary>
    [Fact]
    public void AnExpanderNamedByAnEmptyPropertyElement_IsReported()
    {
        Write("EmptyElement.axaml",
            $"<UserControl {NamespaceHeader}><Expander Header=\"Proxy\">"
            + "<AutomationProperties.Name></AutomationProperties.Name>"
            + "</Expander></UserControl>");

        XamlRuleResult result = Run();

        Assert.Equal(1, result.Inspected);
        Assert.Single(result.Findings);
    }

    /// <summary>
    /// ⛔ An empty name is not a name. It satisfies a presence check and announces nothing, which
    /// is the failure the rule exists to prevent.
    /// </summary>
    [Fact]
    public void AnExpanderWithABlankName_IsReported()
    {
        Write("Blank.axaml",
            $"<UserControl {NamespaceHeader}><Expander AutomationProperties.Name=\"  \" /></UserControl>");

        Assert.Single(Run().Findings);
    }

    /// <summary>
    /// ⛔ <c>Inspected</c> is what proves a zero means "looked and found nothing" rather than
    /// "never looked".
    /// </summary>
    [Fact]
    public void MarkupWithNoExpanders_InspectsNothingAndReportsNothing()
    {
        Write("None.axaml", $"<UserControl {NamespaceHeader}><TextBlock Text=\"hi\" /></UserControl>");

        XamlRuleResult result = Run();

        Assert.Equal(0, result.Inspected);
        Assert.Empty(result.Findings);
    }

    /// <summary>
    /// ⛔ A build copies markup into <c>obj</c>. A scan that includes it reports every violation
    /// twice, and keeps reporting one after the source is fixed until someone cleans.
    /// </summary>
    [Fact]
    public void MarkupUnderObj_IsNotScanned()
    {
        Directory.CreateDirectory(Path.Combine(_root, "obj"));
        File.WriteAllText(
            Path.Combine(_root, "obj", "Copy.axaml"),
            $"<UserControl {NamespaceHeader}><Expander /></UserControl>");

        Assert.Equal(0, Run().Inspected);
    }

    /// <summary>
    /// The rule matches on local name, so it works against WPF and MAUI markup too — a
    /// fully-qualified match would make it silently inert on both.
    /// </summary>
    [Fact]
    public void WpfMarkup_IsScannedToo()
    {
        Write("Wpf.xaml",
            "<UserControl xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" "
            + "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\"><Expander /></UserControl>");

        Assert.Equal(1, Run().Inspected);
    }
}
