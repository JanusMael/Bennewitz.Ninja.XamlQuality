using Bennewitz.Ninja.XamlQuality;
using Bennewitz.Ninja.XamlQuality.Rules;
using Xunit;

namespace XamlQuality.Tests.Rules;

/// <summary>
/// <see cref="InteractiveAutomationIdRule"/> against markup written for the test.
/// </summary>
/// <remarks>
/// ⭐ <b>What counts as an id was measured, not assumed.</b> On Avalonia 12.1.3, through the
/// framework's runtime XAML loader: <c>x:Name</c> alone gives a derived id, an empty attribute gives
/// an empty one that hides it, and an empty property element sets nothing.
/// </remarks>
public sealed class InteractiveAutomationIdRuleTests : IDisposable
{
    private readonly string _root;

    public InteractiveAutomationIdRuleTests()
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

    private XamlRuleResult Run(params string[] additional)
        => new InteractiveAutomationIdRule(additional).Analyze(XamlScanContext.Load(_root));

    private const string NamespaceHeader =
        "xmlns=\"https://github.com/avaloniaui\" " +
        "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\"";

    [Fact]
    public void AButtonWithNoAutomationId_IsReported()
    {
        Write("Bad.axaml", $"<UserControl {NamespaceHeader}><Button Content=\"Save\" /></UserControl>");

        XamlRuleResult result = Run();

        Assert.Equal(1, result.Inspected);
        XamlFinding finding = Assert.Single(result.Findings);
        Assert.Equal("BNXQ1007", finding.RuleId);
        Assert.Contains("Button", finding.Message, StringComparison.Ordinal);
        Assert.Contains("no explicit AutomationId", finding.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AButtonWithAnAutomationId_IsNotReported()
    {
        Write("Good.axaml",
            $"<UserControl {NamespaceHeader}>"
            + "<Button Content=\"Save\" AutomationProperties.AutomationId=\"SaveButton\" />"
            + "</UserControl>");

        XamlRuleResult result = Run();

        Assert.Empty(result.Findings);
        Assert.Equal(1, result.Inspected);
    }

    /// <summary>
    /// ⛔ The framework derives an id from <c>x:Name</c>, and the guide's rule 1 says not to rely on
    /// it: renaming the field renames what every test searches for.
    /// </summary>
    [Fact]
    public void XName_IsNotAnExplicitId()
    {
        Write("Named.axaml",
            $"<UserControl {NamespaceHeader}><Button x:Name=\"SaveButton\" Content=\"Save\" /></UserControl>");

        XamlRuleResult result = Run();

        Assert.Equal(1, result.Inspected);
        Assert.Single(result.Findings);
    }

    /// <summary>The name a person reads is a different property, and does not stand in for the id.</summary>
    [Fact]
    public void AnAutomationName_IsNotAnId()
    {
        Write("NameOnly.axaml",
            $"<UserControl {NamespaceHeader}>"
            + "<Button Content=\"Save\" AutomationProperties.Name=\"Save the file\" />"
            + "</UserControl>");

        XamlRuleResult result = Run();

        Assert.Single(result.Findings);
    }

    [Fact]
    public void ThePropertyElementSpelling_Counts()
    {
        Write("Element.axaml",
            $"<UserControl {NamespaceHeader}><Button Content=\"Save\">"
            + "<AutomationProperties.AutomationId>SaveButton</AutomationProperties.AutomationId>"
            + "</Button></UserControl>");

        XamlRuleResult result = Run();

        Assert.Equal(1, result.Inspected);
        Assert.Empty(result.Findings);
    }

    [Fact]
    public void ABoundAutomationId_Counts()
    {
        Write("Bound.axaml",
            $"<UserControl {NamespaceHeader}>"
            + "<Button Content=\"Save\" AutomationProperties.AutomationId=\"{Binding Key}\" />"
            + "</UserControl>");

        XamlRuleResult result = Run();

        Assert.Empty(result.Findings);
    }

    /// <summary>
    /// A property element holding a binding sets an id, although it has no text of its own.
    /// </summary>
    [Fact]
    public void APropertyElementHoldingABinding_Counts()
    {
        Write("BoundElement.axaml",
            $"<UserControl {NamespaceHeader}><Button Content=\"Save\">"
            + "<AutomationProperties.AutomationId><Binding Path=\"Key\" /></AutomationProperties.AutomationId>"
            + "</Button></UserControl>");

        XamlRuleResult result = Run();

        Assert.Equal(1, result.Inspected);
        Assert.Empty(result.Findings);
    }

    /// <summary>
    /// ⛔ Measured on Avalonia 12.1.3: an empty or blank attribute is what the control reports as its
    /// id, which hides even the one the framework would derive from <c>x:Name</c>. The finding says
    /// the id is empty rather than missing, because that is the fix the reader needs.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void AnEmptyOrBlankAttribute_IsReportedAsEmpty(string value)
    {
        Write("Empty.axaml",
            $"<UserControl {NamespaceHeader}>"
            + $"<Button x:Name=\"SaveButton\" Content=\"Save\" AutomationProperties.AutomationId=\"{value}\" />"
            + "</UserControl>");

        XamlRuleResult result = Run();

        Assert.Equal(1, result.Inspected);
        XamlFinding finding = Assert.Single(result.Findings);
        Assert.Contains("empty AutomationId", finding.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// ⛔ Measured on Avalonia 12.1.3: an empty or self-closing property element leaves the attached
    /// value unset, so it declares no id at all.
    /// </summary>
    [Theory]
    [InlineData("<AutomationProperties.AutomationId></AutomationProperties.AutomationId>")]
    [InlineData("<AutomationProperties.AutomationId />")]
    [InlineData("<AutomationProperties.AutomationId>   </AutomationProperties.AutomationId>")]
    public void AnEmptyPropertyElement_IsNotAnId(string element)
    {
        Write("EmptyElement.axaml",
            $"<UserControl {NamespaceHeader}><Button Content=\"Save\">{element}</Button></UserControl>");

        XamlRuleResult result = Run();

        Assert.Equal(1, result.Inspected);
        XamlFinding finding = Assert.Single(result.Findings);
        Assert.Contains("no explicit AutomationId", finding.Message, StringComparison.Ordinal);
    }

    /// <summary>MAUI's id is a plain property on the element, not an attached one.</summary>
    [Fact]
    public void MauisPlainAutomationId_Counts()
    {
        Write("Page.xaml",
            "<ContentPage xmlns=\"http://schemas.microsoft.com/dotnet/2021/maui\">"
            + "<Button Text=\"Save\" AutomationId=\"SaveButton\" />"
            + "</ContentPage>");

        XamlRuleResult result = Run();

        Assert.Equal(1, result.Inspected);
        Assert.Empty(result.Findings);
    }

    [Fact]
    public void MauisPropertyElementSpelling_Counts()
    {
        Write("Page.xaml",
            "<ContentPage xmlns=\"http://schemas.microsoft.com/dotnet/2021/maui\">"
            + "<Button Text=\"Save\"><Button.AutomationId>SaveButton</Button.AutomationId></Button>"
            + "</ContentPage>");

        XamlRuleResult result = Run();

        Assert.Equal(1, result.Inspected);
        Assert.Empty(result.Findings);
    }

    [Fact]
    public void WpfMarkup_IsScannedToo()
    {
        Write("Window.xaml",
            "<Window xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\">"
            + "<StackPanel><Button Content=\"Save\" /><TextBox AutomationProperties.AutomationId=\"Path\" /></StackPanel>"
            + "</Window>");

        XamlRuleResult result = Run();

        Assert.Equal(2, result.Inspected);
        Assert.Single(result.Findings);
    }

    /// <summary>
    /// Unlike <see cref="InteractiveAutomationNameRuleTests.Expander_IsLeftToItsOwnRule"/>: no other
    /// rule checks an Expander's id, so this one does.
    /// </summary>
    [Fact]
    public void AnExpander_IsCovered()
    {
        Write("Exp.axaml", $"<UserControl {NamespaceHeader}><Expander Header=\"Proxy\" /></UserControl>");

        XamlRuleResult result = Run();

        Assert.Equal(1, result.Inspected);
        Assert.Single(result.Findings);
    }

    [Fact]
    public void AConsumersOwnControl_IsInvisibleUntilItIsNamed()
    {
        Write("Custom.axaml", $"<UserControl {NamespaceHeader}><DiffMinimap /></UserControl>");

        XamlRuleResult ignored = Run();
        Assert.Empty(ignored.Findings);
        Assert.Equal(0, ignored.Inspected);

        XamlRuleResult covered = Run("DiffMinimap");
        Assert.Single(covered.Findings);
        Assert.Equal(1, covered.Inspected);
    }

    /// <summary>
    /// ⚠ Guards the set itself: every framework element <see cref="InteractiveAutomationNameRule"/>
    /// covers, and <c>Expander</c>, is inspected here. Every test above still passes if the set is
    /// emptied down to <c>Button</c>, <c>TextBox</c> and <c>Expander</c>.
    /// </summary>
    [Fact]
    public void EveryFrameworkInteractiveElementAndExpander_IsInspected()
    {
        string[] elements = [.. InteractiveAutomationNameRule.FrameworkInteractiveElements, "Expander"];
        Write("All.axaml",
            $"<UserControl {NamespaceHeader}><StackPanel>"
            + string.Concat(elements.Select(name => $"<{name} />"))
            + "</StackPanel></UserControl>");

        XamlRuleResult result = Run();

        Assert.Equal(elements.Length, result.Inspected);
        Assert.Equal(elements.Length, result.Findings.Count);
    }

    [Fact]
    public void MarkupWithNoInteractiveControls_InspectsNothing()
    {
        Write("Static.axaml",
            $"<UserControl {NamespaceHeader}><StackPanel><TextBlock Text=\"Ready\" /><Border /></StackPanel></UserControl>");

        XamlRuleResult result = Run();

        Assert.Equal(0, result.Inspected);
        Assert.Empty(result.Findings);
    }
}
