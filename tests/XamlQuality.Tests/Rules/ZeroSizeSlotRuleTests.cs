using Bennewitz.Ninja.XamlQuality;
using Bennewitz.Ninja.XamlQuality.Rules;
using Xunit;

namespace XamlQuality.Tests.Rules;

/// <summary>
/// <see cref="ZeroSizeSlotRule"/> against markup written for the test.
/// </summary>
/// <remarks>
/// ⭐ <b>What a slot arranges was measured before it was a test.</b> On Avalonia 12.1.3, loaded by the
/// runtime XAML loader into a headless window: the size each zero, bounded, star and spanned slot below
/// arranges, and whether the control in it is still in the automation tree. The WPF cases, and those
/// whose values are bound, were not measured, and are read as markup states them.
/// </remarks>
public sealed class ZeroSizeSlotRuleTests : IDisposable
{
    private readonly string _root;

    public ZeroSizeSlotRuleTests()
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

    private XamlRuleResult Run() => new ZeroSizeSlotRule().Analyze(XamlScanContext.Load(_root));

    private const string NamespaceHeader =
        "xmlns=\"https://github.com/avaloniaui\" " +
        "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\"";

    private void Grid(string attributes, string content, string definitions = "") =>
        Write("View.axaml",
            $"<UserControl {NamespaceHeader}><Grid {attributes}>{definitions}{content}</Grid></UserControl>");

    [Fact]
    public void AControlInAZeroRow_IsReported()
    {
        Grid("RowDefinitions=\"Auto,0,*\"", "<TextBlock Text=\"top\" /><Button Grid.Row=\"1\" Content=\"Go\" />");

        XamlRuleResult result = Run();

        Assert.Equal(1, result.Inspected);
        XamlFinding finding = Assert.Single(result.Findings);
        Assert.Equal("BNXQ1008", finding.RuleId);
        Assert.Contains("Button is placed in a Grid row of no height", finding.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ThePropertyElementSpelling_Counts()
    {
        Grid(string.Empty, "<Button Grid.Row=\"1\" />",
            "<Grid.RowDefinitions><RowDefinition Height=\"Auto\" /><RowDefinition Height=\"0\" /></Grid.RowDefinitions>");

        XamlRuleResult result = Run();

        Assert.Single(result.Findings);
    }

    [Fact]
    public void AControlInAZeroColumn_IsReported()
    {
        Grid("ColumnDefinitions=\"0,*\"", "<Button Grid.Column=\"0\" /><TextBlock Grid.Column=\"1\" Text=\"right\" />");

        XamlRuleResult result = Run();

        XamlFinding finding = Assert.Single(result.Findings);
        Assert.Contains("column of no width", finding.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void WpfMarkup_IsScannedToo()
    {
        Write("Window.xaml",
            "<Window xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\">"
            + "<Grid><Grid.ColumnDefinitions><ColumnDefinition Width=\"0\" /><ColumnDefinition /></Grid.ColumnDefinitions>"
            + "<Button Grid.Column=\"0\" /></Grid></Window>");

        XamlRuleResult result = Run();

        Assert.Single(result.Findings);
    }

    /// <summary>Measured: with <c>IsVisible="False"</c> the control leaves the automation tree.</summary>
    [Fact]
    public void IsVisibleFalse_HidesIt()
    {
        Grid("RowDefinitions=\"Auto,0\"", "<Button Grid.Row=\"1\" IsVisible=\"False\" />");

        XamlRuleResult result = Run();

        Assert.Empty(result.Findings);
        Assert.Equal(1, result.Inspected);
    }

    /// <summary>A binding is how a collapsing pane hides its content.</summary>
    [Fact]
    public void ABoundIsVisible_HidesIt()
    {
        Grid("RowDefinitions=\"Auto,0\"", "<Button Grid.Row=\"1\" IsVisible=\"{Binding PaneOpen}\" />");

        XamlRuleResult result = Run();

        Assert.Empty(result.Findings);
    }

    /// <summary>Measured: an explicit <c>True</c> leaves the control where an unset one does.</summary>
    [Fact]
    public void IsVisibleTrue_DoesNotHideIt()
    {
        Grid("RowDefinitions=\"Auto,0\"", "<Button Grid.Row=\"1\" IsVisible=\"True\" />");

        XamlRuleResult result = Run();

        Assert.Single(result.Findings);
    }

    [Fact]
    public void TheIsVisiblePropertyElement_Counts()
    {
        Grid("RowDefinitions=\"Auto,0\"", "<Button Grid.Row=\"1\"><Button.IsVisible>False</Button.IsVisible></Button>");

        XamlRuleResult result = Run();

        Assert.Empty(result.Findings);
    }

    [Fact]
    public void AnEmptyIsVisiblePropertyElement_HidesNothing()
    {
        Grid("RowDefinitions=\"Auto,0\"", "<Button Grid.Row=\"1\"><Button.IsVisible /></Button>");

        XamlRuleResult result = Run();

        Assert.Single(result.Findings);
    }

    [Theory]
    [InlineData("Collapsed", 0)]
    [InlineData("Hidden", 0)]
    [InlineData("Visible", 1)]
    public void WpfVisibility_HidesItUnlessVisible(string visibility, int expected)
    {
        Write("Window.xaml",
            "<Window xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\">"
            + "<Grid><Grid.RowDefinitions><RowDefinition Height=\"0\" /></Grid.RowDefinitions>"
            + $"<Button Visibility=\"{visibility}\" /></Grid></Window>");

        XamlRuleResult result = Run();

        Assert.Equal(expected, result.Findings.Count);
    }

    /// <summary>Measured: a row of <c>Height="0" MinHeight="20"</c> is arranged 20 tall.</summary>
    [Fact]
    public void ARowsMinHeight_GivesItRoom()
    {
        Grid(string.Empty, "<Button />",
            "<Grid.RowDefinitions><RowDefinition Height=\"0\" MinHeight=\"20\" /><RowDefinition Height=\"*\" /></Grid.RowDefinitions>");

        XamlRuleResult result = Run();

        Assert.Empty(result.Findings);
        Assert.Equal(1, result.Inspected);
    }

    /// <summary>Measured: a <c>MaxHeight</c> of 0 empties a row whatever its height, star and Auto included.</summary>
    [Theory]
    [InlineData("20")]
    [InlineData("*")]
    [InlineData("Auto")]
    public void AMaxHeightOfZero_EmptiesAnyRow(string height)
    {
        Grid(string.Empty, "<Button />",
            $"<Grid.RowDefinitions><RowDefinition Height=\"{height}\" MaxHeight=\"0\" /><RowDefinition Height=\"*\" /></Grid.RowDefinitions>");

        XamlRuleResult result = Run();

        Assert.Single(result.Findings);
    }

    /// <summary>Measured: <c>Height="0" MinHeight="10" MaxHeight="0"</c> is arranged 10 tall.</summary>
    [Fact]
    public void MinHeightWinsOverMaxHeight()
    {
        Grid(string.Empty, "<Button />",
            "<Grid.RowDefinitions><RowDefinition Height=\"0\" MinHeight=\"10\" MaxHeight=\"0\" /><RowDefinition Height=\"*\" /></Grid.RowDefinitions>");

        XamlRuleResult result = Run();

        Assert.Empty(result.Findings);
    }

    /// <summary>Measured: a star of weight 0 is arranged 0 tall, alone or beside another star.</summary>
    [Theory]
    [InlineData("0*,*")]
    [InlineData("Auto,0*")]
    public void AStarOfWeightZero_IsEmpty(string rows)
    {
        Grid($"RowDefinitions=\"{rows}\"", "<Button Grid.Row=\"" + (rows.StartsWith("0*", StringComparison.Ordinal) ? "0" : "1") + "\" />");

        XamlRuleResult result = Run();

        Assert.Single(result.Findings);
    }

    /// <summary>Measured: a span of a 0 row and a 10 row is arranged 10 tall.</summary>
    [Fact]
    public void ASpanWithRoom_IsNotReported()
    {
        Grid("RowDefinitions=\"0,10,*\"", "<Button Grid.RowSpan=\"2\" />");

        XamlRuleResult result = Run();

        Assert.Empty(result.Findings);
        Assert.Equal(1, result.Inspected);
    }

    /// <summary>Measured: two 0 rows with a spacing of 5 between them are arranged 5 tall.</summary>
    [Theory]
    [InlineData("5", 0)]
    [InlineData("0", 1)]
    public void ASpanOfZeroRows_HasTheSpacingBetweenThem(string spacing, int expected)
    {
        Grid($"RowDefinitions=\"0,0,*\" RowSpacing=\"{spacing}\"", "<Button Grid.RowSpan=\"2\" />");

        XamlRuleResult result = Run();

        Assert.Equal(expected, result.Findings.Count);
        if (expected == 1)
        {
            Assert.Contains("2 Grid rows of no height together", result.Findings[0].Message, StringComparison.Ordinal);
        }
    }

    /// <summary>Measured: an index past the last row lands in the last row.</summary>
    [Fact]
    public void AnIndexPastTheLastRow_LandsInIt()
    {
        Grid("RowDefinitions=\"*,0\"", "<Button Grid.Row=\"5\" />");

        XamlRuleResult result = Run();

        Assert.Single(result.Findings);
    }

    /// <summary>
    /// Measured: a control with its own <c>Height</c> in a 0 row overflows at that height, which
    /// <see cref="GridSlotOverflowRule"/> reports. One control is not reported by both.
    /// </summary>
    [Theory]
    [InlineData("Height=\"30\"")]
    [InlineData("MinHeight=\"30\"")]
    public void AControlThatAsksForASize_IsLeftToTheOverflowRule(string size)
    {
        Grid("RowDefinitions=\"Auto,0\"", $"<Button Grid.Row=\"1\" {size} />");

        XamlRuleResult result = Run();

        Assert.Empty(result.Findings);
        Assert.Equal(0, result.Inspected);
    }

    [Fact]
    public void AutoAndStarRows_AreNotSubjects()
    {
        Grid("RowDefinitions=\"Auto,*\"", "<Button /><Button Grid.Row=\"1\" />");

        XamlRuleResult result = Run();

        Assert.Empty(result.Findings);
        Assert.Equal(0, result.Inspected);
    }

    /// <summary>A collapsing pane usually binds its size, which markup cannot evaluate.</summary>
    [Fact]
    public void ABoundRowHeight_IsSkipped()
    {
        Grid(string.Empty, "<Button />",
            "<Grid.RowDefinitions><RowDefinition Height=\"{Binding PaneHeight}\" /></Grid.RowDefinitions>");

        XamlRuleResult result = Run();

        Assert.Empty(result.Findings);
        Assert.Equal(0, result.Inspected);
        XamlSkip skip = Assert.Single(result.Skipped);
        Assert.Equal("Button", skip.Subject);
        Assert.Contains("{Binding PaneHeight}", skip.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void ABoundRowHeight_OnAHiddenControl_IsNotSkipped()
    {
        Grid(string.Empty, "<Button IsVisible=\"{Binding PaneOpen}\" />",
            "<Grid.RowDefinitions><RowDefinition Height=\"{Binding PaneHeight}\" /></Grid.RowDefinitions>");

        XamlRuleResult result = Run();

        Assert.Empty(result.Findings);
        Assert.Empty(result.Skipped);
    }

    [Fact]
    public void ABoundRowBesideARowWithRoom_IsDecidedBySpanning()
    {
        Grid(string.Empty, "<Button Grid.RowSpan=\"2\" />",
            "<Grid.RowDefinitions><RowDefinition Height=\"{Binding PaneHeight}\" /><RowDefinition Height=\"10\" /></Grid.RowDefinitions>");

        XamlRuleResult result = Run();

        Assert.Empty(result.Findings);
        Assert.Empty(result.Skipped);
        Assert.Equal(1, result.Inspected);
    }

    [Fact]
    public void AShorthandThatIsAMarkupExtension_IsSkipped()
    {
        Grid("RowDefinitions=\"{Binding Rows}\"", "<Button />");

        XamlRuleResult result = Run();

        Assert.Empty(result.Findings);
        Assert.Contains("RowDefinitions=\"{Binding Rows}\"", Assert.Single(result.Skipped).Reason, StringComparison.Ordinal);
    }

    /// <summary>A bound bound decides a slot when the rest of it does not.</summary>
    [Theory]
    [InlineData("Height=\"20\" MaxHeight=\"{Binding Cap}\"", "MaxHeight")]
    [InlineData("Height=\"0\" MinHeight=\"{Binding Floor}\"", "MinHeight")]
    public void ABoundBoundThatDecidesTheRow_IsSkipped(string row, string bound)
    {
        Grid(string.Empty, "<Button />",
            $"<Grid.RowDefinitions><RowDefinition {row} /><RowDefinition Height=\"*\" /></Grid.RowDefinitions>");

        XamlRuleResult result = Run();

        Assert.Empty(result.Findings);
        Assert.Contains(bound, Assert.Single(result.Skipped).Reason, StringComparison.Ordinal);
    }

    /// <summary>
    /// Measured: a panel in a 0 row takes what it holds with it, and the fix belongs on the panel, the
    /// control the Grid places.
    /// </summary>
    [Fact]
    public void APanelInAZeroRow_IsReportedForWhatItHolds()
    {
        Grid("RowDefinitions=\"0,*\"", "<Border><StackPanel><Button /><TextBox /></StackPanel></Border>");

        XamlRuleResult result = Run();

        XamlFinding finding = Assert.Single(result.Findings);
        Assert.Contains("This Border", finding.Message, StringComparison.Ordinal);
    }

    /// <summary><c>Infinity</c> is the framework's own default, and caps nothing.</summary>
    [Fact]
    public void AnInfiniteMaxHeight_CapsNothing()
    {
        Grid(string.Empty, "<Button />",
            "<Grid.RowDefinitions><RowDefinition Height=\"20\" MaxHeight=\"Infinity\" /></Grid.RowDefinitions>");

        XamlRuleResult result = Run();

        Assert.Empty(result.Findings);
        Assert.Empty(result.Skipped);
        Assert.Equal(1, result.Inspected);
    }

    /// <summary>A bound index matters only when the rows it could land in differ.</summary>
    [Theory]
    [InlineData("0,10", 1, 0)]
    [InlineData("10,20", 0, 1)]
    public void ABoundRowIndex_IsSkippedOnlyWhereItMatters(string rows, int skipped, int inspected)
    {
        Grid($"RowDefinitions=\"{rows}\"", "<Button Grid.Row=\"{Binding Row}\" />");

        XamlRuleResult result = Run();

        Assert.Empty(result.Findings);
        Assert.Equal(skipped, result.Skipped.Count);
        Assert.Equal(inspected, result.Inspected);
    }

    /// <summary>A bound span matters only when the row it starts in has no room of its own.</summary>
    [Theory]
    [InlineData("0,10", 1, 0)]
    [InlineData("10,0", 0, 1)]
    public void ABoundSpan_IsSkippedOnlyWhereItMatters(string rows, int skipped, int inspected)
    {
        Grid($"RowDefinitions=\"{rows}\"", "<Button Grid.RowSpan=\"{Binding Span}\" />");

        XamlRuleResult result = Run();

        Assert.Empty(result.Findings);
        Assert.Equal(skipped, result.Skipped.Count);
        Assert.Equal(inspected, result.Inspected);
    }

    [Fact]
    public void AControlEmptyBothWays_IsReportedOnce()
    {
        Grid("RowDefinitions=\"0,*\" ColumnDefinitions=\"0,*\"", "<Button />");

        XamlRuleResult result = Run();

        Assert.Single(result.Findings);
        Assert.Equal(1, result.Inspected);
    }
}
