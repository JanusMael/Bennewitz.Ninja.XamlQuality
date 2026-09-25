using Bennewitz.Ninja.XamlQuality;
using Bennewitz.Ninja.XamlQuality.Rules;

namespace XamlQuality.Tests.Rules;

/// <summary>
/// <see cref="KeyBindingFocusRule"/> over markup written for the test, against the miniature
/// framework in <c>FocusFakes.cs</c>.
/// </summary>
/// <remarks>
/// ⭐ <b>The arrangements this rule decides were measured on Avalonia 12.1.3 first</b>, with a key
/// pressed and the command counted: the dead ones ran 0 times, the live ones once. Two were not
/// pressed as written: a dictionary included by path was pressed merged inline instead, and a list
/// that borrows its base's theme key was read from <c>StyledElement.StyleKey</c> at tag 12.1.3. The
/// fakes reproduce the framework's shape, not its behaviour, so what these tests prove is the rule's
/// reading of markup and types; the measurements are what say that reading matches the framework.
/// </remarks>
public sealed class KeyBindingFocusRuleTests : IDisposable
{
    private const string Header =
        "xmlns=\"https://github.com/avaloniaui\" " +
        "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" " +
        "xmlns:local=\"clr-namespace:Somewhere\"";

    private const string Binding = "<KeyBinding Gesture=\"Ctrl+C\" Command=\"{Binding Copy}\" />";

    private const string TextRows =
        "<ListBox.ItemTemplate><DataTemplate><TextBlock Text=\"{Binding}\" /></DataTemplate></ListBox.ItemTemplate>";

    private const string UnfocusableRow =
        "<ControlTheme TargetType=\"ListBoxItem\" BasedOn=\"{StaticResource {x:Type ListBoxItem}}\">"
        + "<Setter Property=\"Focusable\" Value=\"False\" /></ControlTheme>";

    private const string ImplicitUnfocusableRow =
        "<ControlTheme x:Key=\"{x:Type ListBoxItem}\" TargetType=\"ListBoxItem\" BasedOn=\"{StaticResource {x:Type ListBoxItem}}\">"
        + "<Setter Property=\"Focusable\" Value=\"False\" /></ControlTheme>";

    private readonly string _root;

    public KeyBindingFocusRuleTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "xq-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) { Directory.Delete(_root, recursive: true); }
    }

    private void Write(string name, string markup)
    {
        string path = Path.Combine(_root, name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, markup);
    }

    private XamlRuleResult Run() =>
        new KeyBindingFocusRule().Analyze(
            XamlScanContext.Load(_root).WithAssemblies(typeof(FocusFakes.ListBox).Assembly));

    private XamlRuleResult RunWithoutTypes() =>
        new KeyBindingFocusRule().Analyze(XamlScanContext.Load(_root));

    private static string View(string resources, string body) =>
        $"<UserControl {Header}>{resources}{body}</UserControl>";

    private static string List(string inner = "", string attributes = "") =>
        $"<ListBox ItemsSource=\"{{Binding Rows}}\" {attributes}><ListBox.KeyBindings>{Binding}</ListBox.KeyBindings>{inner}</ListBox>";

    private static string UnfocusableRows(string template = TextRows, string attributes = "") =>
        List($"<ListBox.ItemContainerTheme>{UnfocusableRow}</ListBox.ItemContainerTheme>{template}", attributes);

    private static void AssertLive(XamlRuleResult result)
    {
        Assert.Empty(result.Findings);
        Assert.Empty(result.Skipped);
        Assert.Equal(1, result.Inspected);
    }

    private static XamlFinding AssertDead(XamlRuleResult result)
    {
        Assert.Empty(result.Skipped);
        Assert.Equal(1, result.Inspected);
        XamlFinding finding = Assert.Single(result.Findings);
        Assert.Equal("BNXQ1005", finding.RuleId);
        return finding;
    }

    private static XamlSkip AssertSkipped(XamlRuleResult result)
    {
        Assert.Empty(result.Findings);
        Assert.Equal(0, result.Inspected);
        return Assert.Single(result.Skipped);
    }

    // ── Nothing in the path can take focus ─────────────────────────────────────

    /// <summary>
    /// ⭐ The first case: an items control whose containers are presenters, which cannot take focus,
    /// showing text, which cannot either. Measured: 0 runs however it was clicked.
    /// </summary>
    [Fact]
    public void AnItemsControlShowingOnlyText_IsReported()
    {
        Write("View.axaml", View("",
            $"<ItemsControl ItemsSource=\"{{Binding Rows}}\"><ItemsControl.KeyBindings>{Binding}</ItemsControl.KeyBindings>"
            + "<ItemsControl.ItemTemplate><DataTemplate><TextBlock Text=\"{Binding}\" /></DataTemplate></ItemsControl.ItemTemplate>"
            + "</ItemsControl>"));

        XamlFinding finding = AssertDead(Run());

        Assert.Equal("View.axaml", finding.RelativePath);
        Assert.Equal(1, finding.Line);
        Assert.Contains("ContentPresenter", finding.Message, StringComparison.Ordinal);
    }

    /// <summary>⭐ The second case, the one the gotchas entry measured: rows made unfocusable by the list's container theme.</summary>
    [Fact]
    public void AListWhoseItemContainerThemeMakesRowsUnfocusable_IsReported()
    {
        Write("View.axaml", View("", UnfocusableRows()));

        XamlFinding finding = AssertDead(Run());

        Assert.Contains("ListBoxItem", finding.Message, StringComparison.Ordinal);
        Assert.Contains("View.axaml:1", finding.Message, StringComparison.Ordinal);
    }

    /// <summary>The attribute spelling of the container theme, naming a resource.</summary>
    [Fact]
    public void AnItemContainerThemeNamedByResource_IsReported()
    {
        Write("View.axaml", View(
            "<UserControl.Resources><ControlTheme x:Key=\"RowTheme\" TargetType=\"ListBoxItem\">"
            + "<Setter Property=\"Focusable\" Value=\"False\" /></ControlTheme></UserControl.Resources>",
            List(TextRows, "ItemContainerTheme=\"{StaticResource RowTheme}\"")));

        AssertDead(Run());
    }

    /// <summary>
    /// ⭐ TailBlazer's arrangement: the list names a theme, in another file, and THAT theme sets the
    /// container theme. A rule reading only the element itself would never see it.
    /// </summary>
    [Fact]
    public void AnItemContainerThemeSetByTheListsOwnTheme_IsReported()
    {
        Write("Themes/Lines.axaml",
            $"<ResourceDictionary {Header}>"
            + "<ControlTheme x:Key=\"RowTheme\" TargetType=\"ListBoxItem\"><Setter Property=\"Focusable\" Value=\"False\" /></ControlTheme>"
            + "<ControlTheme x:Key=\"LinesTheme\" TargetType=\"ListBox\" BasedOn=\"{StaticResource {x:Type ListBox}}\">"
            + "<Setter Property=\"ItemContainerTheme\" Value=\"{StaticResource RowTheme}\" /></ControlTheme>"
            + "</ResourceDictionary>");
        Write("View.axaml", View("", List(TextRows, "Theme=\"{StaticResource LinesTheme}\"")));

        XamlFinding finding = AssertDead(Run());

        Assert.Contains("Themes/Lines.axaml", finding.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The list's own implicit theme can set the container theme too. Measured: 0 runs.
    /// </summary>
    [Fact]
    public void AnImplicitThemeForTheListThatSetsTheContainerTheme_IsReported()
    {
        Write("View.axaml", View(
            "<UserControl.Resources>"
            + "<ControlTheme x:Key=\"RowTheme\" TargetType=\"ListBoxItem\"><Setter Property=\"Focusable\" Value=\"False\" /></ControlTheme>"
            + "<ControlTheme x:Key=\"{x:Type ListBox}\" TargetType=\"ListBox\"><Setter Property=\"ItemContainerTheme\" Value=\"{StaticResource RowTheme}\" /></ControlTheme>"
            + "</UserControl.Resources>",
            List(TextRows)));

        AssertDead(Run());
    }

    /// <summary>
    /// ⭐ A custom list that borrows its base's theme key gets its base's implicit theme, as TailBlazer's
    /// <c>LinesListBox</c> would: a theme keyed by the custom list's own name would never reach it.
    /// </summary>
    [Fact]
    public void AListThatBorrowsItsBasesThemeKey_GetsItsBasesImplicitTheme()
    {
        Write("View.axaml", View(
            "<UserControl.Resources>"
            + "<ControlTheme x:Key=\"RowTheme\" TargetType=\"ListBoxItem\"><Setter Property=\"Focusable\" Value=\"False\" /></ControlTheme>"
            + "<ControlTheme x:Key=\"{x:Type ListBox}\" TargetType=\"ListBox\"><Setter Property=\"ItemContainerTheme\" Value=\"{StaticResource RowTheme}\" /></ControlTheme>"
            + "</UserControl.Resources>",
            $"<local:BorrowingListBox ItemsSource=\"{{Binding Rows}}\"><local:BorrowingListBox.KeyBindings>{Binding}</local:BorrowingListBox.KeyBindings>"
            + "<local:BorrowingListBox.ItemTemplate><DataTemplate><TextBlock Text=\"{Binding}\" /></DataTemplate></local:BorrowingListBox.ItemTemplate>"
            + "</local:BorrowingListBox>"));

        AssertDead(Run());
    }

    /// <summary>The property-element spelling of a setter's value counts as the attribute does.</summary>
    [Fact]
    public void TheSetterValueElementSpelling_Counts()
    {
        Write("View.axaml", View("", List(
            "<ListBox.ItemContainerTheme><ControlTheme TargetType=\"ListBoxItem\">"
            + "<Setter Property=\"Focusable\"><Setter.Value>False</Setter.Value></Setter></ControlTheme></ListBox.ItemContainerTheme>"
            + TextRows)));

        AssertDead(Run());
    }

    /// <summary>A theme that sets nothing itself inherits what its base sets. Measured: 0 runs.</summary>
    [Fact]
    public void AFocusableInheritedThroughBasedOn_Counts()
    {
        Write("View.axaml", View(
            "<UserControl.Resources>"
            + "<ControlTheme x:Key=\"Base\" TargetType=\"ListBoxItem\"><Setter Property=\"Focusable\" Value=\"False\" /></ControlTheme>"
            + "<ControlTheme x:Key=\"Derived\" TargetType=\"ListBoxItem\" BasedOn=\"{StaticResource Base}\"><Setter Property=\"Padding\" Value=\"2\" /></ControlTheme>"
            + "</UserControl.Resources>",
            List(TextRows, "ItemContainerTheme=\"{StaticResource Derived}\"")));

        AssertDead(Run());
    }

    /// <summary>⭐ The third case: an implicit theme, keyed by type, in an ancestor's resources.</summary>
    [Fact]
    public void AnImplicitThemeInAnAncestorsResources_IsReported()
    {
        Write("View.axaml", View($"<UserControl.Resources>{ImplicitUnfocusableRow}</UserControl.Resources>",
            $"<Border><StackPanel>{List(TextRows)}</StackPanel></Border>"));

        XamlFinding finding = AssertDead(Run());

        Assert.Equal(1, finding.Line);
    }

    /// <summary>The list's own resources reach its containers, which are its logical children. Measured: 0 runs.</summary>
    [Fact]
    public void AnImplicitThemeInTheListsOwnResources_IsReported()
    {
        Write("View.axaml", View("", List($"<ListBox.Resources>{ImplicitUnfocusableRow}</ListBox.Resources>{TextRows}")));

        AssertDead(Run());
    }

    /// <summary>The application's resources reach every view. Measured: 0 runs.</summary>
    [Fact]
    public void AnImplicitThemeInTheApplicationsResources_IsReported()
    {
        Write("App.axaml",
            $"<Application {Header}><Application.Resources>{ImplicitUnfocusableRow}</Application.Resources></Application>");
        Write("Views/View.axaml", View("", List(TextRows)));

        AssertDead(Run());
    }

    /// <summary>A dictionary the application merges in by path is followed to the file it names.</summary>
    [Fact]
    public void AnImplicitThemeInADictionaryTheApplicationIncludes_IsReported()
    {
        Write("App/Themes/Rows.axaml", $"<ResourceDictionary {Header}>{ImplicitUnfocusableRow}</ResourceDictionary>");
        Write("App/App.axaml",
            $"<Application {Header}><Application.Resources><ResourceDictionary><ResourceDictionary.MergedDictionaries>"
            + "<ResourceInclude Source=\"avares://App/Themes/Rows.axaml\" />"
            + "</ResourceDictionary.MergedDictionaries></ResourceDictionary></Application.Resources></Application>");
        Write("App/Views/View.axaml", View("", List(TextRows)));

        AssertDead(Run());
    }

    /// <summary>
    /// ⚠ An explicit container theme <c>BasedOn</c> the type's key inherits whatever implicit theme is in
    /// scope where it is defined, not the framework's. Measured: the implicit theme's False won, 0 runs.
    /// </summary>
    [Fact]
    public void AContainerThemeBasedOnTheImplicitOne_InheritsItsFocusable()
    {
        Write("View.axaml", View($"<UserControl.Resources>{ImplicitUnfocusableRow}</UserControl.Resources>", List(
            "<ListBox.ItemContainerTheme><ControlTheme TargetType=\"ListBoxItem\" BasedOn=\"{StaticResource {x:Type ListBoxItem}}\">"
            + "<Setter Property=\"Padding\" Value=\"2\" /></ControlTheme></ListBox.ItemContainerTheme>" + TextRows)));

        AssertDead(Run());
    }

    /// <summary>Rows written in the markup are their own containers. Measured: 0 runs.</summary>
    [Fact]
    public void RowsDeclaredInMarkupThatCannotTakeFocus_AreReported()
    {
        Write("View.axaml", View("",
            $"<ListBox><ListBox.KeyBindings>{Binding}</ListBox.KeyBindings>"
            + "<ListBoxItem Focusable=\"False\"><TextBlock Text=\"a\" /></ListBoxItem></ListBox>"));

        AssertDead(Run());
    }

    /// <summary>A tab control shows its selected item's content too, and that is read as well.</summary>
    [Fact]
    public void ATabControlWhoseTabsAndContentCannotTakeFocus_IsReported()
    {
        Write("View.axaml", View("",
            $"<TabControl ItemsSource=\"{{Binding Pages}}\"><TabControl.KeyBindings>{Binding}</TabControl.KeyBindings>"
            + "<TabControl.ItemContainerTheme><ControlTheme TargetType=\"TabItem\"><Setter Property=\"Focusable\" Value=\"False\" /></ControlTheme></TabControl.ItemContainerTheme>"
            + "<TabControl.ItemTemplate><DataTemplate><TextBlock Text=\"{Binding Title}\" /></DataTemplate></TabControl.ItemTemplate>"
            + "<TabControl.ContentTemplate><DataTemplate><TextBlock Text=\"{Binding Body}\" /></DataTemplate></TabControl.ContentTemplate>"
            + "</TabControl>"));

        AssertDead(Run());
    }

    // ── Something in the path can take focus ───────────────────────────────────

    /// <summary>
    /// ⛔ The case a cold read gets wrong. A stock list's rows are focusable, but only once their type's
    /// static constructor has registered it; read before that, they report unfocusable, and every
    /// correct list in a codebase would be reported. Measured: a row focused, 1 run.
    /// </summary>
    [Fact]
    public void AListWithStockRows_IsLive_BecauseItsRowsTakeFocus()
    {
        Write("View.axaml", View("", List(TextRows)));

        AssertLive(Run());
    }

    /// <summary>A list that declares itself focusable takes the key itself. Measured: 1 run.</summary>
    [Fact]
    public void AListThatDeclaresItselfFocusable_IsLive()
    {
        Write("View.axaml", View("", UnfocusableRows(attributes: "Focusable=\"True\"")));

        AssertLive(Run());
    }

    /// <summary>
    /// The property-element spelling of <c>Focusable</c> counts as the attribute does.
    /// </summary>
    /// <remarks>
    /// ⚠ The container theme is named by attribute here on purpose. Written as a property element
    /// too, a rule that read neither spelling lost both and still came out live, so this test passed
    /// with the behaviour it names removed.
    /// </remarks>
    [Fact]
    public void ThePropertyElementSpellingOfFocusable_Counts()
    {
        Write("View.axaml", View(
            "<UserControl.Resources><ControlTheme x:Key=\"RowTheme\" TargetType=\"ListBoxItem\">"
            + "<Setter Property=\"Focusable\" Value=\"False\" /></ControlTheme></UserControl.Resources>",
            List("<ListBox.Focusable>True</ListBox.Focusable>" + TextRows, "ItemContainerTheme=\"{StaticResource RowTheme}\"")));

        AssertLive(Run());
    }

    /// <summary>
    /// ⭐ Unfocusable rows around an input: the input takes focus, and the key bubbles through its row to
    /// the list. Measured: clicking a row focused its TextBox, 1 run.
    /// </summary>
    [Fact]
    public void AnInputInTheItemTemplate_KeepsTheBindingLive()
    {
        Write("View.axaml", View("", UnfocusableRows(
            "<ListBox.ItemTemplate><DataTemplate><StackPanel><TextBox Text=\"{Binding}\" /></StackPanel></DataTemplate></ListBox.ItemTemplate>")));

        AssertLive(Run());
    }

    /// <summary>The attribute spelling of the item template, naming a resource, is followed.</summary>
    [Fact]
    public void AnItemTemplateNamedByResource_IsRead()
    {
        Write("View.axaml", View(
            "<UserControl.Resources><DataTemplate x:Key=\"Row\"><TextBox Text=\"{Binding}\" /></DataTemplate></UserControl.Resources>",
            UnfocusableRows(template: string.Empty, attributes: "ItemTemplate=\"{StaticResource Row}\"")));

        AssertLive(Run());
    }

    /// <summary>An items control of buttons is live through the buttons. Measured: 1 run.</summary>
    [Fact]
    public void AnItemsControlOfButtons_IsLive()
    {
        Write("View.axaml", View("",
            $"<ItemsControl ItemsSource=\"{{Binding Rows}}\"><ItemsControl.KeyBindings>{Binding}</ItemsControl.KeyBindings>"
            + "<ItemsControl.ItemTemplate><DataTemplate><Button Content=\"{Binding}\" /></DataTemplate></ItemsControl.ItemTemplate>"
            + "</ItemsControl>"));

        AssertLive(Run());
    }

    /// <summary>Buttons written in the markup are the items themselves. Measured: 1 run.</summary>
    [Fact]
    public void ButtonsDeclaredAsItems_AreLive()
    {
        Write("View.axaml", View("",
            $"<ItemsControl><ItemsControl.KeyBindings>{Binding}</ItemsControl.KeyBindings><Button Content=\"a\" /></ItemsControl>"));

        AssertLive(Run());
    }

    /// <summary>A derived theme's own setter outranks its base's. Measured: 1 run.</summary>
    [Fact]
    public void ADerivedThemeThatRestoresFocusable_IsLive()
    {
        Write("View.axaml", View(
            "<UserControl.Resources>"
            + "<ControlTheme x:Key=\"Base\" TargetType=\"ListBoxItem\"><Setter Property=\"Focusable\" Value=\"False\" /></ControlTheme>"
            + "<ControlTheme x:Key=\"Derived\" TargetType=\"ListBoxItem\" BasedOn=\"{StaticResource Base}\"><Setter Property=\"Focusable\" Value=\"True\" /></ControlTheme>"
            + "</UserControl.Resources>",
            List(TextRows, "ItemContainerTheme=\"{StaticResource Derived}\"")));

        AssertLive(Run());
    }

    /// <summary>A container theme that leaves Focusable alone leaves the rows focusable: TailBlazer's actual state.</summary>
    [Fact]
    public void AContainerThemeThatLeavesFocusableAlone_IsLive()
    {
        Write("View.axaml", View("", List(
            "<ListBox.ItemContainerTheme><ControlTheme TargetType=\"ListBoxItem\"><Setter Property=\"Padding\" Value=\"0\" /></ControlTheme></ListBox.ItemContainerTheme>"
            + TextRows)));

        AssertLive(Run());
    }

    [Fact]
    public void ATreeViewWithStockRows_IsLive()
    {
        Write("View.axaml", View("",
            $"<TreeView ItemsSource=\"{{Binding Nodes}}\"><TreeView.KeyBindings>{Binding}</TreeView.KeyBindings></TreeView>"));

        AssertLive(Run());
    }

    /// <summary>
    /// ⚠ A sibling's resources are not in the list's lookup path. Measured: the theme did not reach it,
    /// and the row took focus, 1 run.
    /// </summary>
    [Fact]
    public void AnImplicitThemeInASiblingsResources_DoesNotReachTheList()
    {
        Write("View.axaml", View("",
            $"<StackPanel><Border><Border.Resources>{ImplicitUnfocusableRow}</Border.Resources></Border>{List(TextRows)}</StackPanel>"));

        AssertLive(Run());
    }

    // ── Not this rule's subject ─────────────────────────────────────────────────

    /// <summary>
    /// Key bindings on a control that is not an items control are not checked, and not listed: deciding
    /// them needs every template beneath them.
    /// </summary>
    [Fact]
    public void KeyBindingsOnAnythingButAnItemsControl_AreNotInspected()
    {
        Write("View.axaml",
            $"<UserControl {Header}><UserControl.KeyBindings>{Binding}</UserControl.KeyBindings>"
            + $"<TextBox><TextBox.KeyBindings>{Binding}</TextBox.KeyBindings></TextBox></UserControl>");

        XamlRuleResult result = Run();

        Assert.Empty(result.Findings);
        Assert.Empty(result.Skipped);
        Assert.Equal(0, result.Inspected);
    }

    [Fact]
    public void AnEmptyKeyBindingsBlock_IsNotABinding()
    {
        Write("View.axaml", View("",
            $"<ListBox><ListBox.KeyBindings /><ListBox.ItemContainerTheme>{UnfocusableRow}</ListBox.ItemContainerTheme>{TextRows}</ListBox>"));

        Assert.Equal(0, Run().Inspected);
    }

    /// <summary>⭐ The zero that means "nothing to check", told apart from the zero that means "clean".</summary>
    [Fact]
    public void MarkupWithNoKeyBindings_InspectsNothing()
    {
        Write("View.axaml", View("", "<ListBox><ListBox.ItemTemplate><DataTemplate><TextBlock /></DataTemplate></ListBox.ItemTemplate></ListBox>"));

        XamlRuleResult result = Run();

        Assert.Empty(result.Findings);
        Assert.Empty(result.Skipped);
        Assert.Equal(0, result.Inspected);
    }

    // ── Seen, but not decided ───────────────────────────────────────────────────

    /// <summary>⛔ A scan copied without WithAssemblies checks nothing, and names every binding it could not check.</summary>
    [Fact]
    public void WithoutAssemblies_EveryKeyBindingIsNamedAsSkipped()
    {
        Write("View.axaml", View("", UnfocusableRows()));

        XamlRuleResult result = RunWithoutTypes();

        XamlSkip skip = AssertSkipped(result);
        Assert.Equal("ListBox", skip.Subject);
        Assert.Contains("given no assemblies", skip.Reason, StringComparison.Ordinal);
        Assert.Contains("WithAssemblies", skip.Reason, StringComparison.Ordinal);
        Assert.Equal("View.axaml", skip.RelativePath);
        Assert.Equal(1, skip.Line);
    }

    [Fact]
    public void AControlNoScannedAssemblyDefines_IsSkipped()
    {
        Write("View.axaml", View("",
            $"<local:Mystery><local:Mystery.KeyBindings>{Binding}</local:Mystery.KeyBindings></local:Mystery>"));

        XamlSkip skip = AssertSkipped(Run());

        Assert.Equal("Mystery", skip.Subject);
    }

    /// <summary>
    /// ⚠ Unfocusable rows and no item template: what the rows show comes from data templates elsewhere,
    /// which may hold an input. Measured both ways: text rows, 0 runs; an input in the template, 1 run.
    /// </summary>
    [Fact]
    public void UnfocusableRowsWithNoItemTemplate_AreSkippedNotReported()
    {
        Write("View.axaml", View("", UnfocusableRows(template: string.Empty)));

        XamlSkip skip = AssertSkipped(Run());

        Assert.Contains("ItemTemplate", skip.Reason, StringComparison.Ordinal);
    }

    /// <summary>
    /// ⛔ A style outranks a theme, in either direction. Measured: a style setting False killed the
    /// chord, and one setting True over a theme's False revived it. Styles are not evaluated, so the
    /// rule stops.
    /// </summary>
    [Fact]
    public void AStyleThatSetsFocusableOnTheRows_StopsTheRule()
    {
        Write("Styles.axaml",
            $"<Styles {Header}><Style Selector=\"ListBox > ListBoxItem\"><Setter Property=\"Focusable\" Value=\"True\" /></Style></Styles>");
        Write("View.axaml", View("", UnfocusableRows()));

        XamlSkip skip = AssertSkipped(Run());

        Assert.Contains("Styles.axaml", skip.Reason, StringComparison.Ordinal);
    }

    /// <summary>
    /// ⭐ A tree's rows present items of their own, and the header their template carries takes focus.
    /// Measured in Fluent, Simple and Semi: rows made unfocusable, a click focused the header, 1 run.
    /// </summary>
    [Fact]
    public void ATreeViewWithUnfocusableRows_IsSkipped_BecauseItsRowsCarryAHeader()
    {
        Write("View.axaml", View("",
            $"<TreeView ItemsSource=\"{{Binding Nodes}}\"><TreeView.KeyBindings>{Binding}</TreeView.KeyBindings>"
            + "<TreeView.ItemContainerTheme><ControlTheme TargetType=\"TreeViewItem\"><Setter Property=\"Focusable\" Value=\"False\" /></ControlTheme></TreeView.ItemContainerTheme>"
            + "<TreeView.ItemTemplate><DataTemplate><TextBlock Text=\"{Binding}\" /></DataTemplate></TreeView.ItemTemplate>"
            + "</TreeView>"));

        XamlSkip skip = AssertSkipped(Run());

        Assert.Contains("header", skip.Reason, StringComparison.Ordinal);
    }

    /// <summary>
    /// ⚠ Something with a template of its own may hold focus the markup cannot show: an Expander is
    /// unfocusable, and its header is not. Measured: the header took focus in all three themes.
    /// </summary>
    [Fact]
    public void ATemplatedControlInTheItemTemplate_IsSkipped()
    {
        Write("View.axaml", View("", UnfocusableRows(
            "<ListBox.ItemTemplate><DataTemplate><Expander><TextBlock Text=\"{Binding}\" /></Expander></DataTemplate></ListBox.ItemTemplate>")));

        XamlSkip skip = AssertSkipped(Run());

        Assert.Contains("Expander", skip.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void ABoundItemContainerTheme_IsSkipped()
    {
        Write("View.axaml", View("", List(TextRows, "ItemContainerTheme=\"{Binding RowTheme}\"")));

        AssertSkipped(Run());
    }

    [Fact]
    public void AContainerThemeTheScanDoesNotHold_IsSkipped()
    {
        Write("View.axaml", View("", List(TextRows, "ItemContainerTheme=\"{StaticResource FromAPackage}\"")));

        XamlSkip skip = AssertSkipped(Run());

        Assert.Contains("FromAPackage", skip.Reason, StringComparison.Ordinal);
    }

    /// <summary>
    /// ⚠ An implicit theme in another view's resources reaches this list only if this view is placed
    /// inside that one, which markup does not say.
    /// </summary>
    [Fact]
    public void AnImplicitThemeInAnotherViewsResources_IsSkipped()
    {
        Write("Shell.axaml", View($"<UserControl.Resources>{ImplicitUnfocusableRow}</UserControl.Resources>", "<ContentControl />"));
        Write("View.axaml", View("", List(TextRows)));

        XamlSkip skip = AssertSkipped(Run());

        Assert.Contains("Shell.axaml", skip.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void ARowThemeThatChangesFocusableInAState_IsSkipped()
    {
        Write("View.axaml", View("", List(
            "<ListBox.ItemContainerTheme><ControlTheme TargetType=\"ListBoxItem\">"
            + "<Setter Property=\"Focusable\" Value=\"False\" />"
            + "<Style Selector=\"^:selected\"><Setter Property=\"Focusable\" Value=\"True\" /></Style>"
            + "</ControlTheme></ListBox.ItemContainerTheme>" + TextRows)));

        XamlSkip skip = AssertSkipped(Run());

        Assert.Contains("state", skip.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void ATabControlWithNoContentTemplate_IsSkipped()
    {
        Write("View.axaml", View("",
            $"<TabControl ItemsSource=\"{{Binding Pages}}\"><TabControl.KeyBindings>{Binding}</TabControl.KeyBindings>"
            + "<TabControl.ItemContainerTheme><ControlTheme TargetType=\"TabItem\"><Setter Property=\"Focusable\" Value=\"False\" /></ControlTheme></TabControl.ItemContainerTheme>"
            + "<TabControl.ItemTemplate><DataTemplate><TextBlock Text=\"{Binding Title}\" /></DataTemplate></TabControl.ItemTemplate>"
            + "</TabControl>"));

        XamlSkip skip = AssertSkipped(Run());

        Assert.Contains("ContentTemplate", skip.Reason, StringComparison.Ordinal);
    }
}
