using Bennewitz.Ninja.XamlQuality;
using Bennewitz.Ninja.XamlQuality.Rules;

namespace XamlQuality.Tests.Rules;

/// <summary>
/// <see cref="GridSlotOverflowRule"/> against markup written for the test.
/// </summary>
/// <remarks>
/// ⭐ <b>The four shapes come from the port that found the defect</b>, and the last two are the
/// ones that matter: a rule which fires on a slot larger than the child, or on a star row whose
/// size markup cannot know, would report false positives on correct layouts — and a rule people
/// stop reading is worse than no rule.
/// </remarks>
public sealed class GridSlotOverflowRuleTests : IDisposable
{
    private readonly string _root;

    public GridSlotOverflowRuleTests()
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

    private XamlRuleResult Run() => new GridSlotOverflowRule().Analyze(XamlScanContext.Load(_root));

    private const string Header =
        "xmlns=\"https://github.com/avaloniaui\" " +
        "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\"";

    private string Grid(string definitions, string children) =>
        $"<UserControl {Header}><Grid {definitions}>{children}</Grid></UserControl>";

    [Fact]
    public void AZeroRowHoldingAMinHeight_IsReported()
    {
        Write("Bad.axaml", Grid("RowDefinitions=\"*,0\"",
            "<Border Grid.Row=\"1\" MinHeight=\"50\" />"));

        XamlRuleResult result = Run();

        XamlFinding finding = Assert.Single(result.Findings);
        Assert.Equal("XQ1004", finding.RuleId);
        Assert.Contains("IsVisible", finding.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AFixedRowSmallerThanTheChild_IsReported()
    {
        Write("Small.axaml", Grid("RowDefinitions=\"20\"",
            "<Border Grid.Row=\"0\" MinHeight=\"50\" />"));

        Assert.Single(Run().Findings);
    }

    /// <summary>⚠ A slot with room to spare is correct markup and must stay silent.</summary>
    [Fact]
    public void AFixedRowLargerThanTheChild_IsNotReported()
    {
        Write("Roomy.axaml", Grid("RowDefinitions=\"200\"",
            "<Border Grid.Row=\"0\" MinHeight=\"50\" />"));

        XamlRuleResult result = Run();

        Assert.Empty(result.Findings);
        Assert.Equal(1, result.Inspected);
    }

    /// <summary>
    /// ⛔ A star row resolves against its siblings and the available space, so markup cannot know
    /// its size. Guessing here fires on correct layouts.
    /// </summary>
    [Fact]
    public void AStarRow_IsNotDecidableAndIsNotReported()
    {
        Write("Star.axaml", Grid("RowDefinitions=\"*\"",
            "<Border Grid.Row=\"0\" MinHeight=\"50\" />"));

        XamlRuleResult result = Run();

        Assert.Empty(result.Findings);
        Assert.Equal(1, result.Inspected);
    }

    /// <summary>⛔ Auto sizes against content, which markup cannot know either.</summary>
    [Fact]
    public void AnAutoRow_IsNotDecidableAndIsNotReported()
    {
        Write("Auto.axaml", Grid("RowDefinitions=\"Auto\"",
            "<Border Grid.Row=\"0\" MinHeight=\"50\" />"));

        Assert.Empty(Run().Findings);
    }

    /// <summary>
    /// ⚠ The spelling that goes unhandled. Every assertion above uses the attribute shorthand, so
    /// without this one the rule could be inert on markup written the other way and look clean.
    /// </summary>
    [Fact]
    public void ThePropertyElementSpellingOfRowDefinitions_Counts()
    {
        Write("Element.axaml", Grid(string.Empty,
            "<Grid.RowDefinitions><RowDefinition Height=\"0\" /></Grid.RowDefinitions>"
            + "<Border Grid.Row=\"0\" MinHeight=\"50\" />"));

        Assert.Single(Run().Findings);
    }

    [Fact]
    public void AColumnTooNarrowForItsChild_IsReported()
    {
        Write("Col.axaml", Grid("ColumnDefinitions=\"10,*\"",
            "<Border Grid.Column=\"0\" MinWidth=\"64\" />"));

        Assert.Single(Run().Findings);
    }

    /// <summary>⚠ An explicit Height overflows a fixed row exactly as a MinHeight does.</summary>
    [Fact]
    public void AnExplicitHeight_CountsLikeAMinHeight()
    {
        Write("Explicit.axaml", Grid("RowDefinitions=\"0\"",
            "<Border Grid.Row=\"0\" Height=\"40\" />"));

        Assert.Single(Run().Findings);
    }

    /// <summary>⚠ An unset Grid.Row means row 0, as the framework treats it.</summary>
    [Fact]
    public void AnOmittedGridRow_MeansRowZero()
    {
        Write("Implicit.axaml", Grid("RowDefinitions=\"0,*\"",
            "<Border MinHeight=\"50\" />"));

        Assert.Single(Run().Findings);
    }

    /// <summary>
    /// ⛔ Grid.Row binds to DIRECT children. An element nested in another panel is positioned by
    /// that panel, so reading it against this Grid's rows would be measuring the wrong slot.
    /// </summary>
    [Fact]
    public void AGrandchildInsideAnotherPanel_IsNotAttributedToTheOuterGrid()
    {
        Write("Nested.axaml", Grid("RowDefinitions=\"0,*\"",
            "<StackPanel Grid.Row=\"1\"><Border MinHeight=\"50\" /></StackPanel>"));

        XamlRuleResult result = Run();

        Assert.Empty(result.Findings);
        Assert.Equal(1, result.Inspected);
    }

    /// <summary>
    /// ⭐ The one that separates "nothing is wrong" from "nothing was checked". Every assertion
    /// above still passes if the selector stops matching Grid entirely.
    /// </summary>
    [Fact]
    public void MarkupWithNoGrid_InspectsNothing()
    {
        Write("None.axaml", $"<UserControl {Header}><Border MinHeight=\"50\" /></UserControl>");

        XamlRuleResult result = Run();

        Assert.Empty(result.Findings);
        Assert.Equal(0, result.Inspected);
    }
}
