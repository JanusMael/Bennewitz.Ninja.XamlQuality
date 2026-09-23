using Bennewitz.Ninja.XamlQuality;
using Bennewitz.Ninja.XamlQuality.Rules;
using Xunit;

namespace XamlQuality.Tests.Rules;

/// <summary>
/// <see cref="InteractiveAutomationNameRule"/> against markup written for the test.
/// </summary>
/// <remarks>
/// ⭐ <b>Three of these assert things the guard this rule replaced could not.</b> That guard
/// scanned its own repository and asserted "zero offenders today", so it could never show that it
/// would find one, never show that an empty name is not a name, and — the one that actually bit —
/// never show the difference between finding nothing wrong and looking at nothing.
/// </remarks>
public sealed class InteractiveAutomationNameRuleTests : IDisposable
{
    private readonly string _root;

    public InteractiveAutomationNameRuleTests()
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
        => new InteractiveAutomationNameRule(additional).Analyze(XamlScanContext.Load(_root));

    private const string NamespaceHeader =
        "xmlns=\"https://github.com/avaloniaui\" " +
        "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\"";

    [Fact]
    public void AnUnnamedButton_IsReported()
    {
        Write("Bad.axaml", $"<UserControl {NamespaceHeader}><Button Content=\"Save\" /></UserControl>");

        XamlRuleResult result = Run();

        Assert.Equal(1, result.Inspected);
        XamlFinding finding = Assert.Single(result.Findings);
        Assert.Equal("XQ1002", finding.RuleId);
        Assert.Contains("Button", finding.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ANamedButton_IsNotReported()
    {
        Write("Good.axaml",
            $"<UserControl {NamespaceHeader}>"
            + "<Button Content=\"Save\" AutomationProperties.Name=\"Save the file\" />"
            + "</UserControl>");

        XamlRuleResult result = Run();

        Assert.Empty(result.Findings);
        Assert.Equal(1, result.Inspected);
    }

    /// <summary>
    /// ⛔ An empty name satisfies a presence check and announces nothing. Measured on a real
    /// codebase: its accessibility guard stayed green with <c>AutomationProperties.Name=""</c> in
    /// the markup, because it only asked whether the attribute was there.
    /// </summary>
    [Fact]
    public void AnEmptyAutomationName_IsNotAName()
    {
        Write("Empty.axaml",
            $"<UserControl {NamespaceHeader}><Button Content=\"Save\" AutomationProperties.Name=\"\" /></UserControl>");

        XamlRuleResult result = Run();

        Assert.Single(result.Findings);
    }

    [Fact]
    public void ThePropertyElementSpelling_Counts()
    {
        Write("Element.axaml",
            $"<UserControl {NamespaceHeader}><Button Content=\"Save\">"
            + "<AutomationProperties.Name>Save the file</AutomationProperties.Name>"
            + "</Button></UserControl>");

        XamlRuleResult result = Run();

        Assert.Empty(result.Findings);
    }

    /// <summary>
    /// ⛔ The element spelling has to clear the same bar as the attribute. An empty or blank
    /// property element announces exactly what <c>Name=""</c> does, which is nothing.
    /// </summary>
    [Theory]
    [InlineData("<AutomationProperties.Name></AutomationProperties.Name>")]
    [InlineData("<AutomationProperties.Name />")]
    [InlineData("<AutomationProperties.Name>   </AutomationProperties.Name>")]
    public void AnEmptyPropertyElement_IsNotAName(string element)
    {
        Write("EmptyElement.axaml",
            $"<UserControl {NamespaceHeader}><Button Content=\"Save\">{element}</Button></UserControl>");

        XamlRuleResult result = Run();

        Assert.Equal(1, result.Inspected);
        Assert.Single(result.Findings);
    }

    /// <summary>
    /// And the check for the case above must not over-reach: a property element holding a binding
    /// is a name, although it has no text of its own.
    /// </summary>
    [Fact]
    public void APropertyElementHoldingABinding_Counts()
    {
        Write("BoundElement.axaml",
            $"<UserControl {NamespaceHeader}><Button Content=\"Save\">"
            + "<AutomationProperties.Name><Binding Path=\"Title\" /></AutomationProperties.Name>"
            + "</Button></UserControl>");

        XamlRuleResult result = Run();

        Assert.Equal(1, result.Inspected);
        Assert.Empty(result.Findings);
    }

    /// <summary>
    /// ⭐ The one that separates "nothing is wrong" from "nothing was checked". A consumer's own
    /// control is invisible to the framework list, so the rule walks straight past it and reports
    /// clean over exactly the markup least likely to have been reviewed.
    /// </summary>
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

    [Fact]
    public void Expander_IsLeftToItsOwnRule()
    {
        Write("Exp.axaml", $"<UserControl {NamespaceHeader}><Expander Header=\"Proxy\" /></UserControl>");

        XamlRuleResult result = Run();

        // XQ1001 owns this one; reporting it here too would show one defect twice.
        Assert.Empty(result.Findings);
        Assert.Equal(0, result.Inspected);
    }

    [Fact]
    public void TheFrameworkSet_IsNotSilentlyEmpty()
    {
        // ⚠ Guards the set itself. Every assertion above still passes if the list is emptied and
        // the names are passed in by hand, which would leave the shipped default covering nothing.
        Assert.Contains("Button", InteractiveAutomationNameRule.FrameworkInteractiveElements);
        Assert.Contains("MenuItem", InteractiveAutomationNameRule.FrameworkInteractiveElements);
        Assert.DoesNotContain("Expander", InteractiveAutomationNameRule.FrameworkInteractiveElements);
    }
}
