using Bennewitz.Ninja.XamlQuality;
using Bennewitz.Ninja.XamlQuality.Rules;

namespace XamlQuality.Tests.Rules;

/// <summary>
/// <see cref="GridSlotOverflowRule"/> against markup written for the test.
/// </summary>
/// <remarks>
/// <para>
/// ⭐ <b>The four shapes come from the port that found the defect</b>, and the last two are the
/// ones that matter: a rule which fires on a slot larger than the child, or on a star row whose
/// size markup cannot know, would report false positives on correct layouts — and a rule people
/// stop reading is worse than no rule.
/// </para>
/// <para>
/// ⭐ <b>Inspected means measured.</b> In the shapes a consumer reported, a binding or a resource
/// stood between a control and its slot, and each counted as inspected and clean although nothing
/// was measured. Those tests assert Findings, Inspected and Skipped together, because any one of
/// them alone passes while the other two are wrong.
/// </para>
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

    /// <summary>Nothing reported, nothing counted, and the one skip, returned for its reason.</summary>
    private static XamlSkip OnlySkipped(XamlRuleResult result)
    {
        Assert.Empty(result.Findings);
        Assert.Equal(0, result.Inspected);
        return Assert.Single(result.Skipped);
    }

    /// <summary>Nothing reported, counted or skipped: the rule had nothing to measure.</summary>
    private static void Neither(XamlRuleResult result)
    {
        Assert.Empty(result.Findings);
        Assert.Equal(0, result.Inspected);
        Assert.Empty(result.Skipped);
    }

    /// <summary>Reported, counted, and nothing skipped.</summary>
    private static void MeasuredAndReported(XamlRuleResult result)
    {
        Assert.Single(result.Findings);
        Assert.Equal(1, result.Inspected);
        Assert.Empty(result.Skipped);
    }

    [Fact]
    public void AZeroRowHoldingAMinHeight_IsReported()
    {
        Write("Bad.axaml", Grid("RowDefinitions=\"*,0\"",
            "<Border Grid.Row=\"1\" MinHeight=\"50\" />"));

        XamlRuleResult result = Run();

        XamlFinding finding = Assert.Single(result.Findings);
        Assert.Equal("BNXQ1004", finding.RuleId);
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
    /// its size. Guessing here fires on correct layouts. Nor is it inspected or skipped: nothing was
    /// measured, and no value stood in the way.
    /// </summary>
    [Fact]
    public void AStarRow_IsNotDecidableAndIsNotReported()
    {
        Write("Star.axaml", Grid("RowDefinitions=\"*\"",
            "<Border Grid.Row=\"0\" MinHeight=\"50\" />"));

        Neither(Run());
    }

    /// <summary>⛔ Auto sizes against content, which markup cannot know either.</summary>
    [Fact]
    public void AnAutoRow_IsNotDecidableAndIsNotReported()
    {
        Write("Auto.axaml", Grid("RowDefinitions=\"Auto\"",
            "<Border Grid.Row=\"0\" MinHeight=\"50\" />"));

        Neither(Run());
    }

    /// <summary>⚠ The framework reads Auto in any case, so a lowercase one is no unknown value.</summary>
    [Fact]
    public void AnAutoRowInLowerCase_IsStillAuto()
    {
        Write("LowerAuto.axaml", Grid("RowDefinitions=\"auto\"",
            "<Border MinHeight=\"50\" />"));

        Neither(Run());
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

    /// <summary>⚠ A definition that states no size is a star, as the framework makes it.</summary>
    [Fact]
    public void ADefinitionWithNoSize_IsAStar()
    {
        Write("Unsized.axaml", Grid(string.Empty,
            "<Grid.ColumnDefinitions><ColumnDefinition /></Grid.ColumnDefinitions>"
            + "<Border MinWidth=\"40\" />"));

        Neither(Run());
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
    /// that panel, so reading it against this Grid's rows would be measuring the wrong slot. The
    /// panel declares a height that fits its row, so it is measured, and the count shows the grid
    /// was read at all.
    /// </summary>
    [Fact]
    public void AGrandchildInsideAnotherPanel_IsNotAttributedToTheOuterGrid()
    {
        Write("Nested.axaml", Grid("RowDefinitions=\"0,40\"",
            "<StackPanel Grid.Row=\"1\" Height=\"40\"><Border MinHeight=\"50\" /></StackPanel>"));

        XamlRuleResult result = Run();

        Assert.Empty(result.Findings);
        Assert.Equal(1, result.Inspected);
    }

    /// <summary>
    /// ⛔ A span's room is every slot it crosses. Measured against its first column alone, a control
    /// that fits the two columns it spans was reported: a false positive on correct markup.
    /// </summary>
    [Fact]
    public void AChildThatFitsTheColumnsItSpans_IsNotReported()
    {
        Write("SpanFits.axaml", Grid("ColumnDefinitions=\"50,50\"",
            "<Border Grid.ColumnSpan=\"2\" Width=\"80\" />"));

        XamlRuleResult result = Run();

        Assert.Empty(result.Findings);
        Assert.Equal(1, result.Inspected);
    }

    [Fact]
    public void AChildWiderThanTheColumnsItSpans_IsReported()
    {
        Write("SpanTooNarrow.axaml", Grid("ColumnDefinitions=\"50,50\"",
            "<Border Grid.ColumnSpan=\"2\" Width=\"120\" />"));

        XamlFinding finding = Assert.Single(Run().Findings);
        Assert.Contains("2 columns fixed at 100 together", finding.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AChildThatFitsTheRowsItSpans_IsNotReported()
    {
        Write("RowSpanFits.axaml", Grid("RowDefinitions=\"30,30\"",
            "<Border Grid.RowSpan=\"2\" MinHeight=\"50\" />"));

        Assert.Empty(Run().Findings);
    }

    /// <summary>⛔ A span across a star column is as undecidable as the star column itself.</summary>
    [Fact]
    public void ASpanAcrossAStarColumn_IsNotDecidableAndIsNotReported()
    {
        Write("SpanStar.axaml", Grid("ColumnDefinitions=\"50,*\"",
            "<Border Grid.ColumnSpan=\"2\" Width=\"500\" />"));

        Neither(Run());
    }

    /// <summary>⚠ The spacing between the columns a control spans is room it gets.</summary>
    [Fact]
    public void ColumnSpacing_CountsInsideASpan()
    {
        Write("SpanSpacing.axaml", Grid("ColumnDefinitions=\"50,50\" ColumnSpacing=\"10\"",
            "<Border Grid.ColumnSpan=\"2\" Width=\"105\" />"));

        Assert.Empty(Run().Findings);
    }

    /// <summary>
    /// ⛔ The framework does not validate spacing, and applies a negative one as it is, overlapping
    /// the columns a span crosses. It is a literal to measure, never a value markup cannot evaluate.
    /// </summary>
    [Fact]
    public void ANegativeSpacing_IsMeasured()
    {
        Write("NegativeSpacing.axaml", Grid("ColumnDefinitions=\"50,50\" ColumnSpacing=\"-10\"",
            "<Border Grid.ColumnSpan=\"2\" Width=\"95\" />"));

        XamlRuleResult result = Run();

        XamlFinding finding = Assert.Single(result.Findings);
        Assert.Contains("2 columns fixed at 90 together", finding.Message, StringComparison.Ordinal);
        Assert.Equal(1, result.Inspected);
        Assert.Empty(result.Skipped);
    }

    /// <summary>⚠ A spacing that is not a finite number gives a span no room it can measure.</summary>
    [Fact]
    public void ASpacingThatIsNotAFiniteNumber_IsNamedInSkipped()
    {
        Write("NaNSpacing.axaml", Grid("ColumnDefinitions=\"50,50\" ColumnSpacing=\"NaN\"",
            "<Border Grid.ColumnSpan=\"2\" Width=\"500\" />"));

        XamlSkip skip = OnlySkipped(Run());
        Assert.Contains("ColumnSpacing=\"NaN\"", skip.Reason, StringComparison.Ordinal);
    }

    /// <summary>
    /// ⛔ Bound spacing is not knowable from markup, so a span across it is not measured, and the
    /// binding is named as what stopped it.
    /// </summary>
    [Fact]
    public void BoundSpacing_MakesASpanUndecidable()
    {
        Write("SpanBoundSpacing.axaml", Grid("ColumnDefinitions=\"50,50\" ColumnSpacing=\"{Binding Gap}\"",
            "<Border Grid.ColumnSpan=\"2\" Width=\"500\" />"));

        XamlSkip skip = OnlySkipped(Run());
        Assert.Contains("ColumnSpacing=\"{Binding Gap}\"", skip.Reason, StringComparison.Ordinal);
    }

    /// <summary>⚠ Spacing written as a property element that holds a resource is as unknown.</summary>
    [Fact]
    public void SpacingFromAResourceElement_IsNamedInSkipped()
    {
        Write("SpacingElement.axaml", Grid("ColumnDefinitions=\"50,50\"",
            "<Grid.ColumnSpacing><StaticResource ResourceKey=\"Gap\" /></Grid.ColumnSpacing>"
            + "<Border Grid.ColumnSpan=\"2\" Width=\"500\" />"));

        XamlSkip skip = OnlySkipped(Run());
        Assert.Contains("Grid.ColumnSpacing holding <StaticResource>", skip.Reason, StringComparison.Ordinal);
    }

    /// <summary>
    /// ⛔ The framework places an index past the last column IN the last column, so that is the slot
    /// to measure. Skipping it reported nothing where the control really overflows.
    /// </summary>
    [Fact]
    public void AnIndexPastTheLastColumn_IsMeasuredAgainstTheLastColumn()
    {
        Write("PastTheEnd.axaml", Grid("ColumnDefinitions=\"200,50\"",
            "<Border Grid.Column=\"9\" Width=\"80\" />"));

        Assert.Single(Run().Findings);
    }

    /// <summary>⚠ A span running off the grid's edge covers only the columns that remain.</summary>
    [Fact]
    public void ASpanPastTheLastColumn_IsClampedToTheColumnsThatRemain()
    {
        Write("SpanPastTheEnd.axaml", Grid("ColumnDefinitions=\"50,50,50\"",
            "<Border Grid.Column=\"2\" Grid.ColumnSpan=\"3\" Width=\"80\" />"));

        XamlFinding finding = Assert.Single(Run().Findings);
        Assert.Contains("a column fixed at 50", finding.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// ⛔ A bound span is not knowable from markup, so the control is not measured, and the binding
    /// is named as what stopped it.
    /// </summary>
    [Fact]
    public void ABoundSpan_IsNamedInSkipped()
    {
        Write("SpanBound.axaml", Grid("ColumnDefinitions=\"50,50\"",
            "<Border Grid.ColumnSpan=\"{Binding Span}\" Width=\"500\" />"));

        XamlSkip skip = OnlySkipped(Run());
        Assert.Contains("Grid.ColumnSpan=\"{Binding Span}\"", skip.Reason, StringComparison.Ordinal);
    }

    /// <summary>
    /// ⭐ Definitions bound as a whole. Counted as inspected, the control read as checked and clean
    /// although no slot size was known. A binding with a comma inside it is one value, not two
    /// slots.
    /// </summary>
    [Fact]
    public void BoundColumnDefinitions_AreNamedInSkipped()
    {
        Write("Bound.axaml", Grid("ColumnDefinitions=\"{Binding Cols, Mode=OneWay}\"",
            "<Border Grid.Column=\"1\" MinWidth=\"40\" />"));

        XamlSkip skip = OnlySkipped(Run());
        Assert.Contains("ColumnDefinitions=\"{Binding Cols, Mode=OneWay}\"", skip.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void ColumnDefinitionsFromAResource_AreNamedInSkipped()
    {
        Write("Resource.axaml", Grid("ColumnDefinitions=\"{StaticResource Cols}\"",
            "<Border Grid.Column=\"1\" MinWidth=\"40\" />"));

        XamlSkip skip = OnlySkipped(Run());
        Assert.Contains("ColumnDefinitions=\"{StaticResource Cols}\"", skip.Reason, StringComparison.Ordinal);
    }

    /// <summary>⚠ The property-element spelling, with the control's own column sized by a resource.</summary>
    [Fact]
    public void AColumnWidthFromAResource_IsNamedInSkipped()
    {
        Write("SlotResource.axaml", Grid(string.Empty,
            "<Grid.ColumnDefinitions><ColumnDefinition Width=\"Auto\" /><ColumnDefinition Width=\"{DynamicResource W}\" /></Grid.ColumnDefinitions>"
            + "<Border Grid.Column=\"1\" MinWidth=\"40\" />"));

        XamlSkip skip = OnlySkipped(Run());
        Assert.Contains("column 1's Width=\"{DynamicResource W}\"", skip.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void ABoundColumn_IsNamedInSkipped()
    {
        Write("BoundIndex.axaml", Grid("ColumnDefinitions=\"Auto,16,*\"",
            "<Border Grid.Column=\"{Binding C}\" MinWidth=\"40\" />"));

        XamlSkip skip = OnlySkipped(Run());
        Assert.Contains("Grid.Column=\"{Binding C}\"", skip.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void ABoundMinWidth_IsNamedInSkipped()
    {
        Write("BoundSize.axaml", Grid("ColumnDefinitions=\"Auto,16,*\"",
            "<Border Grid.Column=\"1\" MinWidth=\"{Binding M}\" />"));

        XamlSkip skip = OnlySkipped(Run());
        Assert.Contains("MinWidth=\"{Binding M}\"", skip.Reason, StringComparison.Ordinal);
    }

    /// <summary>⚠ Text that is not a size is no more measurable than a binding.</summary>
    [Fact]
    public void AColumnWidthThatIsNotASize_IsNamedInSkipped()
    {
        Write("NotASize.axaml", Grid("ColumnDefinitions=\"16,banana\"",
            "<Border Grid.Column=\"1\" MinWidth=\"40\" />"));

        XamlSkip skip = OnlySkipped(Run());
        Assert.Contains("column 1's width \"banana\"", skip.Reason, StringComparison.Ordinal);
    }

    /// <summary>
    /// ⚠ A size the framework rejects is not one to measure: an infinite or a negative width is
    /// named like any other text that is not a size.
    /// </summary>
    [Fact]
    public void SizesTheFrameworkRejects_AreNamedInSkipped()
    {
        Write("Rejected.axaml", Grid("ColumnDefinitions=\"16\"",
            "<Border Width=\"Infinity\" /><Border Width=\"-5\" />"));

        XamlRuleResult result = Run();

        Assert.Empty(result.Findings);
        Assert.Equal(0, result.Inspected);
        Assert.Equal(2, result.Skipped.Count);
    }

    /// <summary>
    /// ⛔ A value markup cannot evaluate stops only the check that depends on it. A bound width on
    /// another column leaves this one measurable, and it overflows.
    /// </summary>
    [Fact]
    public void ABoundWidthOnAnotherColumn_DoesNotStopTheCheck()
    {
        Write("OtherBound.axaml", Grid(string.Empty,
            "<Grid.ColumnDefinitions><ColumnDefinition Width=\"{Binding W}\" /><ColumnDefinition Width=\"16\" /></Grid.ColumnDefinitions>"
            + "<Border Grid.Column=\"1\" MinWidth=\"40\" />"));

        MeasuredAndReported(Run());
    }

    [Fact]
    public void AnUnreadableWidthOnAnotherColumn_DoesNotStopTheCheck()
    {
        Write("OtherNotASize.axaml", Grid("ColumnDefinitions=\"16,banana\"",
            "<Border Grid.Column=\"0\" MinWidth=\"40\" />"));

        MeasuredAndReported(Run());
    }

    /// <summary>
    /// ⚠ What a control asks for is its MinWidth when that is a literal size, else its Width. A
    /// MinWidth markup cannot evaluate leaves the literal Width to measure, and this one overflows.
    /// </summary>
    [Fact]
    public void ABoundMinWidthBesideALiteralWidth_IsMeasuredByTheWidth()
    {
        Write("Fallback.axaml", Grid("ColumnDefinitions=\"16\"",
            "<Border MinWidth=\"{Binding M}\" Width=\"100\" />"));

        MeasuredAndReported(Run());
    }

    /// <summary>⚠ Every value that stands in the way is named, so fixing one does not reveal the next.</summary>
    [Fact]
    public void EveryValueThatStopsTheCheck_IsNamed()
    {
        Write("Several.axaml", Grid("ColumnDefinitions=\"16,16\"",
            "<Border Grid.ColumnSpan=\"{Binding S}\" MinWidth=\"{Binding M}\" Width=\"{Binding W}\" />"));

        XamlSkip skip = OnlySkipped(Run());
        Assert.Contains("Grid.ColumnSpan=\"{Binding S}\"", skip.Reason, StringComparison.Ordinal);
        Assert.Contains("MinWidth=\"{Binding M}\"", skip.Reason, StringComparison.Ordinal);
        Assert.Contains(" Width=\"{Binding W}\"", skip.Reason, StringComparison.Ordinal);
    }

    /// <summary>⭐ A skip names the control, its file and its line, so a consumer can key on it and a reader can find it.</summary>
    [Fact]
    public void ASkip_NamesTheControlAndWhereItIs()
    {
        Write("Where.axaml",
            $"<UserControl {Header}>\n"
            + "  <Grid ColumnDefinitions=\"{Binding Cols}\">\n"
            + "    <Border Grid.Column=\"1\" MinWidth=\"40\" />\n"
            + "  </Grid>\n"
            + "</UserControl>\n");

        XamlSkip skip = OnlySkipped(Run());
        Assert.Equal("Border", skip.Subject);
        Assert.Equal("Where.axaml", skip.RelativePath);
        Assert.Equal(3, skip.Line);
    }

    /// <summary>⛔ A control that declares no size has nothing to measure, however fixed its slot.</summary>
    [Fact]
    public void AChildThatDeclaresNoSize_IsNotInspected()
    {
        Write("NoSize.axaml", Grid("ColumnDefinitions=\"16\"", "<Border />"));

        Neither(Run());
    }

    /// <summary>⛔ Nor is it skipped in a bound grid: no value could give it anything to measure.</summary>
    [Fact]
    public void AChildThatDeclaresNoSize_IsNotSkippedInABoundGrid()
    {
        Write("NoSizeBound.axaml", Grid("ColumnDefinitions=\"{Binding Cols}\"",
            "<Border Grid.Column=\"1\" />"));

        Neither(Run());
    }

    /// <summary>⚠ <c>Width="NaN"</c> is how a control says its width is unset: no size, not a size of NaN.</summary>
    [Fact]
    public void AWidthOfNaN_DeclaresNoSize()
    {
        Write("NaN.axaml", Grid("ColumnDefinitions=\"16\"", "<Border Width=\"NaN\" />"));

        Neither(Run());
    }

    /// <summary>
    /// ⛔ A value is named only where a fixed slot could depend on it. Among nothing but Auto and
    /// star columns, a bound index lands in one of them whatever it turns out to be.
    /// </summary>
    [Fact]
    public void ABoundColumnAmongOnlyAutoAndStarColumns_IsNotSkipped()
    {
        Write("NoFixed.axaml", Grid("ColumnDefinitions=\"Auto,*\"",
            "<Border Grid.Column=\"{Binding C}\" MinWidth=\"40\" />"));

        Neither(Run());
    }

    /// <summary>⛔ A span that starts in an Auto column crosses it, whatever its length.</summary>
    [Fact]
    public void ABoundSpanFromAnAutoColumn_IsNotSkipped()
    {
        Write("SpanFromAuto.axaml", Grid("ColumnDefinitions=\"Auto,16\"",
            "<Border Grid.ColumnSpan=\"{Binding S}\" MinWidth=\"40\" />"));

        Neither(Run());
    }

    /// <summary>⛔ A bound size in a star column: a literal one would not be measured there either.</summary>
    [Fact]
    public void ABoundMinWidthInAStarColumn_IsNotSkipped()
    {
        Write("StarBound.axaml", Grid("ColumnDefinitions=\"16,*\"",
            "<Border Grid.Column=\"1\" MinWidth=\"{Binding M}\" />"));

        Neither(Run());
    }

    /// <summary>
    /// ⚠ Measured one way and not the other, a control is inspected AND skipped: the count says
    /// something was checked, and the skip says what was not.
    /// </summary>
    [Fact]
    public void AControlMeasuredOneWayOnly_IsInspectedAndSkipped()
    {
        Write("OneWay.axaml", Grid("RowDefinitions=\"{Binding Rows}\" ColumnDefinitions=\"16\"",
            "<Border MinHeight=\"10\" MinWidth=\"40\" />"));

        XamlRuleResult result = Run();

        Assert.Single(result.Findings);
        Assert.Equal(1, result.Inspected);
        XamlSkip skip = Assert.Single(result.Skipped);
        Assert.Contains("RowDefinitions=\"{Binding Rows}\"", skip.Reason, StringComparison.Ordinal);
    }

    /// <summary>⚠ Inspected counts controls, not directions: one measured both ways counts once.</summary>
    [Fact]
    public void AControlMeasuredBothWays_CountsOnce()
    {
        Write("BothWays.axaml", Grid("RowDefinitions=\"20\" ColumnDefinitions=\"20\"",
            "<Border MinHeight=\"10\" MinWidth=\"10\" />"));

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
