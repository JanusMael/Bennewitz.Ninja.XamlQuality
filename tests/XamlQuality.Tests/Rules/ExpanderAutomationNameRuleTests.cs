using Bennewitz.Ninja.XamlQuality;
using Bennewitz.Ninja.XamlQuality.Rules;

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
[TestClass]
public sealed class ExpanderAutomationNameRuleTests
{
    private string _root = null!;

    [TestInitialize]
    public void Init()
    {
        _root = Path.Combine(Path.GetTempPath(), "xq-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    [TestCleanup]
    public void Cleanup()
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

    [TestMethod]
    public void AnUnnamedExpander_IsReported()
    {
        Write("Bad.axaml", $"<UserControl {NamespaceHeader}><Expander Header=\"Proxy\" /></UserControl>");

        XamlRuleResult result = Run();

        Assert.AreEqual(1, result.Inspected, "The rule must have examined the Expander.");
        Assert.AreEqual(1, result.Findings.Count);
        Assert.AreEqual("XQ1001", result.Findings[0].RuleId);
        StringAssert.Contains(result.Findings[0].RelativePath, "Bad.axaml");
    }

    [TestMethod]
    public void ANamedExpander_IsNotReported()
    {
        Write("Good.axaml",
            $"<UserControl {NamespaceHeader}>"
            + "<Expander Header=\"Proxy\" AutomationProperties.Name=\"Proxy settings\" />"
            + "</UserControl>");

        XamlRuleResult result = Run();

        Assert.AreEqual(1, result.Inspected);
        Assert.AreEqual(0, result.Findings.Count);
    }

    /// <summary>
    /// ⚠ The property-ELEMENT spelling. Checking only the attribute form is how this rule would
    /// report a violation against markup that is actually correct.
    /// </summary>
    [TestMethod]
    public void AnExpanderNamedByPropertyElement_IsNotReported()
    {
        Write("Element.axaml",
            $"<UserControl {NamespaceHeader}><Expander Header=\"Proxy\">"
            + "<AutomationProperties.Name>Proxy settings</AutomationProperties.Name>"
            + "</Expander></UserControl>");

        XamlRuleResult result = Run();

        Assert.AreEqual(0, result.Findings.Count, "The property-element spelling sets the name too.");
    }

    /// <summary>
    /// ⛔ An empty name is not a name. It satisfies a presence check and announces nothing, which
    /// is the failure the rule exists to prevent — so the rule must not accept it.
    /// </summary>
    [TestMethod]
    public void AnExpanderWithABlankName_IsReported()
    {
        Write("Blank.axaml",
            $"<UserControl {NamespaceHeader}><Expander AutomationProperties.Name=\"  \" /></UserControl>");

        Assert.AreEqual(1, Run().Findings.Count);
    }

    /// <summary>
    /// ⛔ The rule must be inert on markup with no Expanders — and <c>Inspected</c> is what proves
    /// the zero means "looked and found nothing" rather than "never looked".
    /// </summary>
    [TestMethod]
    public void MarkupWithNoExpanders_InspectsNothingAndReportsNothing()
    {
        Write("None.axaml", $"<UserControl {NamespaceHeader}><TextBlock Text=\"hi\" /></UserControl>");

        XamlRuleResult result = Run();

        Assert.AreEqual(0, result.Inspected);
        Assert.AreEqual(0, result.Findings.Count);
    }

    /// <summary>
    /// ⛔ A build copies markup into <c>obj</c>. A scan that includes it reports every violation
    /// twice, and keeps reporting one after the source is fixed until someone cleans.
    /// </summary>
    [TestMethod]
    public void MarkupUnderObj_IsNotScanned()
    {
        Directory.CreateDirectory(Path.Combine(_root, "obj"));
        File.WriteAllText(
            Path.Combine(_root, "obj", "Copy.axaml"),
            $"<UserControl {NamespaceHeader}><Expander /></UserControl>");

        XamlRuleResult result = Run();

        Assert.AreEqual(0, result.Inspected, "Build output must not be scanned.");
    }

    /// <summary>
    /// The rule matches on local name, so it works against WPF and MAUI markup too — a
    /// fully-qualified match would make it silently inert on both.
    /// </summary>
    [TestMethod]
    public void WpfMarkup_IsScannedToo()
    {
        Write("Wpf.xaml",
            "<UserControl xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" "
            + "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\"><Expander /></UserControl>");

        Assert.AreEqual(1, Run().Inspected, ".xaml is markup as much as .axaml is.");
    }
}
