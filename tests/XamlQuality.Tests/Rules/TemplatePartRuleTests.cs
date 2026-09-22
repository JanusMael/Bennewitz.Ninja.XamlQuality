using Bennewitz.Ninja.XamlQuality;
using Bennewitz.Ninja.XamlQuality.Rules;
using Xunit;

namespace XamlQuality.Tests.Rules;

/// <summary>A control whose parts this suite's markup is written against.</summary>
/// <remarks>
/// ⭐ Declared here rather than mocked: the rule reads <b>public string constants</b> off real
/// types, so the only faithful fixture is a real type with real constants.
/// </remarks>
public sealed class ProxyPanel
{
    /// <summary>A part the themes below either declare or forget.</summary>
    public const string HeaderPart = "PART_Header";

    /// <summary>A second, so a theme can satisfy one and miss the other.</summary>
    public const string FooterPart = "PART_Footer";

    /// <summary>Not a part; it must not be mistaken for one.</summary>
    public const string NotAPart = "Header";
}

/// <summary>
/// <see cref="TemplatePartRule"/> over markup and types written for the test.
/// </summary>
public sealed class TemplatePartRuleTests : IDisposable
{
    private readonly string _root;

    public TemplatePartRuleTests()
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

    private XamlRuleResult Run() =>
        new TemplatePartRule().Analyze(
            XamlScanContext.Load(_root).WithAssemblies(typeof(ProxyPanel).Assembly));

    private XamlRuleResult RunWithoutTypes() =>
        new TemplatePartRule().Analyze(XamlScanContext.Load(_root));

    private const string Header =
        "xmlns=\"https://github.com/avaloniaui\" " +
        "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" " +
        "xmlns:t=\"clr-namespace:XamlQuality.Tests.Rules\"";

    private static string Theme(string target, string inner) =>
        $"<ResourceDictionary {Header}><ControlTheme x:Key=\"k\" TargetType=\"{target}\">"
        + $"<Setter Property=\"Template\"><ControlTemplate>{inner}</ControlTemplate></Setter>"
        + "</ControlTheme></ResourceDictionary>";

    [Fact]
    public void AThemeMissingAPart_IsReported()
    {
        Write("Bad.axaml", Theme("t:ProxyPanel", "<Border Name=\"PART_Header\" />"));

        XamlRuleResult result = Run();

        Assert.Equal(2, result.Inspected);
        XamlFinding finding = Assert.Single(result.Findings);
        Assert.Equal("XQ1003", finding.RuleId);
        Assert.Contains("PART_Footer", finding.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AThemeDeclaringEveryPart_IsClean()
    {
        Write("Good.axaml",
            Theme("t:ProxyPanel", "<Border Name=\"PART_Header\" /><Border Name=\"PART_Footer\" />"));

        XamlRuleResult result = Run();

        Assert.Empty(result.Findings);
        Assert.Equal(2, result.Inspected);
    }

    [Fact]
    public void TheXTypeSpelling_IsUnderstood()
    {
        Write("XType.axaml",
            Theme("{x:Type t:ProxyPanel}", "<Border Name=\"PART_Header\" /><Border Name=\"PART_Footer\" />"));

        XamlRuleResult result = Run();

        Assert.Empty(result.Findings);
        Assert.Equal(2, result.Inspected);
    }

    /// <summary>
    /// ⛔ The case a file-wide match gets wrong: two themes in one dictionary, each satisfying the
    /// other's lookups. Scoped per ControlTheme, the second is still short a part.
    /// </summary>
    [Fact]
    public void OneThemeDoesNotSatisfyAnother_InTheSameFile()
    {
        Write("Two.axaml",
            $"<ResourceDictionary {Header}>"
            + "<ControlTheme x:Key=\"a\" TargetType=\"t:ProxyPanel\">"
            + "<Setter Property=\"Template\"><ControlTemplate>"
            + "<Border Name=\"PART_Header\" /><Border Name=\"PART_Footer\" />"
            + "</ControlTemplate></Setter></ControlTheme>"
            + "<ControlTheme x:Key=\"b\" TargetType=\"t:ProxyPanel\">"
            + "<Setter Property=\"Template\"><ControlTemplate>"
            + "<Border Name=\"PART_Header\" />"
            + "</ControlTemplate></Setter></ControlTheme>"
            + "</ResourceDictionary>");

        XamlRuleResult result = Run();

        Assert.Single(result.Findings);
        Assert.Equal(4, result.Inspected);
    }

    /// <summary>
    /// ⚠ A name that exists only for a style selector is correct markup, not a violation. Reporting
    /// it would make the rule noisy on exactly the themes that are well written.
    /// </summary>
    [Fact]
    public void APartNamedOnlyForAStyleSelector_IsNotAViolation()
    {
        Write("Styled.axaml",
            Theme("t:ProxyPanel",
                "<Border Name=\"PART_Header\" /><Border Name=\"PART_Footer\" /><Border Name=\"PART_Badge\" />"));

        XamlRuleResult result = Run();

        Assert.Empty(result.Findings);
    }

    [Fact]
    public void AConstantThatIsNotAPartName_IsIgnored()
    {
        Write("Bare.axaml",
            Theme("t:ProxyPanel", "<Border Name=\"PART_Header\" /><Border Name=\"PART_Footer\" />"));

        XamlRuleResult result = Run();

        // ProxyPanel.NotAPart is "Header"; counting it would make Inspected 3.
        Assert.Equal(2, result.Inspected);
    }

    [Fact]
    public void AControlWithNoThemeInTheScan_IsSkippedNotReported()
    {
        Write("Other.axaml", Theme("t:SomethingElse", "<Border />"));

        XamlRuleResult result = Run();

        Assert.Empty(result.Findings);
        Assert.Equal(0, result.Inspected);
    }

    /// <summary>
    /// ⭐ The distinction the whole <c>Inspected</c> field exists for. A consumer who forgets
    /// <c>WithAssemblies</c> gets zero findings — and must be able to tell that from agreement.
    /// </summary>
    [Fact]
    public void WithoutAssemblies_TheRuleReportsThatItCheckedNothing()
    {
        Write("Bad.axaml", Theme("t:ProxyPanel", "<Border Name=\"PART_Header\" />"));

        XamlRuleResult result = RunWithoutTypes();

        Assert.Empty(result.Findings);
        Assert.Equal(0, result.Inspected);
    }
}
