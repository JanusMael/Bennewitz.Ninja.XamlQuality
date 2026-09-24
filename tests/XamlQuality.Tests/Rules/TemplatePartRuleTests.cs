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

/// <summary>Stands in for a framework's name scope, so the controls below read like real ones.</summary>
public static class FakeNameScope
{
    /// <summary>A lookup by name, shaped like <c>INameScope.Find</c>.</summary>
    /// <param name="name">The name to find.</param>
    /// <returns>Nothing; only the call's shape matters to the rule.</returns>
    public static object? Find(string name)
    {
        _ = name;
        return null;
    }
}

/// <summary>Looks its part up with an inline literal, the way DiffView's <c>DiffPanePresenter</c> did.</summary>
public sealed class InlineLookupPanel
{
    /// <summary>Stands in for <c>OnApplyTemplate</c>: the literal goes straight to a lookup.</summary>
    /// <returns>Whatever the lookup returns.</returns>
    public static object? ApplyTemplate() => FakeNameScope.Find("PART_Inline");
}

/// <summary>Looks its part up inside a lambda, which compiles into a nested type.</summary>
public sealed class LambdaLookupPanel
{
    /// <summary>The literal lives in a compiler-generated closure, not in this type's own methods.</summary>
    public static Func<object?> ApplyTemplate { get; } = () => FakeNameScope.Find("PART_InLambda");
}

/// <summary>One part as a constant and one inline: the mixed case constants alone could not see.</summary>
public sealed class MixedLookupPanel
{
    /// <summary>The part declared the checkable way.</summary>
    public const string ConstantPart = "PART_Constant";

    /// <summary>The part that is only a literal.</summary>
    /// <returns>Whatever the lookup returns.</returns>
    public static object? ApplyTemplate() => FakeNameScope.Find("PART_Literal");
}

/// <summary>Has a theme and no parts at all.</summary>
public sealed class PartlessPanel
{
}

/// <summary>Not public, and keeping its part in a private constant, as many controls do.</summary>
internal sealed class InternalLookupPanel
{
    private const string Part = "PART_Private";

    internal static object? ApplyTemplate() => FakeNameScope.Find(Part);
}

/// <summary>Names an element the way compiled XAML does: the literal feeds a setter, not a lookup.</summary>
public sealed class NamingPanel
{
    /// <summary>Shaped like the <c>Name</c> property compiled XAML sets.</summary>
    public string? Name { get; set; }

    /// <summary>Names an element. Nothing looks this name up, so no theme owes it.</summary>
    public void Build() => Name = "PART_Named";
}

/// <summary>Builds its part names at runtime, from the bare prefix and from a fragment.</summary>
public sealed class RuntimeNamedPanel
{
    // A constant holding only the prefix: read as a name, it would be a part called "PART_".
    private const string Prefix = "PART_";

    /// <summary>Two lookups whose names are assembled, as for numbered parts.</summary>
    /// <param name="suffix">Appended to the bare prefix.</param>
    /// <param name="index">Appended to a fragment.</param>
    /// <returns>Whatever the lookups return.</returns>
    public static object? ApplyTemplate(string suffix, int index) =>
        FakeNameScope.Find(Prefix + suffix) ?? FakeNameScope.Find("PART_Row" + index);
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

    // ── Parts declared in compiled code, not only in public constants ──────────

    /// <summary>
    /// ⭐ The case DiffView found. A lookup written inline, <c>NameScope.Find("PART_…")</c>, never
    /// reached a constant, so a rule reading public constants alone reported the control clean while
    /// checking none of it. The literal is read from the compiled code.
    /// </summary>
    [Fact]
    public void AnInlineLiteralLookup_IsChecked()
    {
        Write("Inline.axaml", Theme("t:InlineLookupPanel", "<Border Name=\"PART_Other\" />"));

        XamlRuleResult result = Run();

        Assert.Equal(1, result.Inspected);
        XamlFinding finding = Assert.Single(result.Findings);
        Assert.Contains("PART_Inline", finding.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AnInlineLiteralLookup_ThatTheThemeDeclares_IsClean()
    {
        Write("Inline.axaml", Theme("t:InlineLookupPanel", "<Border Name=\"PART_Inline\" />"));

        XamlRuleResult result = Run();

        Assert.Empty(result.Findings);
        Assert.Equal(1, result.Inspected);
    }

    /// <summary>A lambda's literal compiles into a nested type, and still belongs to its control.</summary>
    [Fact]
    public void ALiteralInsideALambda_IsCreditedToItsControl()
    {
        Write("Lambda.axaml", Theme("t:LambdaLookupPanel", "<Border />"));

        XamlFinding finding = Assert.Single(Run().Findings);

        Assert.Contains("LambdaLookupPanel", finding.Message, StringComparison.Ordinal);
        Assert.Contains("PART_InLambda", finding.Message, StringComparison.Ordinal);
    }

    /// <summary>The case constants could never have caught: one part declared, one only looked up.</summary>
    [Fact]
    public void AMixedControl_HasItsLiteralCheckedBesideItsConstant()
    {
        Write("Mixed.axaml", Theme("t:MixedLookupPanel", "<Border Name=\"PART_Constant\" />"));

        XamlRuleResult result = Run();

        Assert.Equal(2, result.Inspected);
        XamlFinding finding = Assert.Single(result.Findings);
        Assert.Contains("PART_Literal", finding.Message, StringComparison.Ordinal);
    }

    /// <summary>A control that is not public, with its part in a private constant, is read too.</summary>
    [Fact]
    public void AnInternalControlWithAPrivateConstant_IsChecked()
    {
        Write("Internal.axaml", Theme("t:InternalLookupPanel", "<Border />"));

        XamlFinding finding = Assert.Single(Run().Findings);

        Assert.Contains("PART_Private", finding.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// ⛔ What compiled XAML does with every element name: the literal feeds <c>set_Name</c> or
    /// <c>Register</c>, not a lookup. Read as a lookup, it made every compiled theme in a real
    /// codebase look like a control with parts.
    /// </summary>
    [Fact]
    public void ALiteralThatNamesAnElement_IsNotALookup()
    {
        Write("Naming.axaml", Theme("t:NamingPanel", "<Border />"));

        XamlRuleResult result = Run();

        Assert.Empty(result.Findings);
        Assert.Single(result.Skipped, s => s.Subject == nameof(NamingPanel));
    }

    /// <summary>
    /// A name assembled at runtime is not read as a part: neither the bare prefix nor a fragment
    /// that is concatenated before the lookup. Either would be reported as a part no theme declares.
    /// </summary>
    [Fact]
    public void ANameBuiltAtRuntime_IsNotReadAsAPart()
    {
        Write("Runtime.axaml", Theme("t:RuntimeNamedPanel", "<Border />"));

        XamlRuleResult result = Run();

        Assert.Empty(result.Findings);
        Assert.Single(result.Skipped, s => s.Subject == nameof(RuntimeNamedPanel));
    }

    // ── What was seen but not checked ───────────────────────────────────────────

    /// <summary>
    /// ⭐ A themed control with no parts adds nothing to <c>Inspected</c>, so without
    /// <c>Skipped</c> it is indistinguishable from one that was checked and found clean.
    /// </summary>
    [Fact]
    public void AThemedControlWithNoParts_IsListedAsSkipped()
    {
        Write("Partless.axaml", Theme("t:PartlessPanel", "<Border />"));

        XamlRuleResult result = Run();

        Assert.Empty(result.Findings);
        XamlSkip skip = Assert.Single(result.Skipped, s => s.Subject == nameof(PartlessPanel));
        Assert.Equal("Partless.axaml", skip.RelativePath);
        Assert.NotNull(skip.Line);
    }

    /// <summary>
    /// A control whose parts are known but whose theme is not in the scan used to lower
    /// <c>Inspected</c> and say no more. It is named now.
    /// </summary>
    [Fact]
    public void AControlWithPartsButNoThemeInTheScan_IsListedAsSkipped()
    {
        Write("Other.axaml", Theme("t:SomethingElse", "<Border />"));

        XamlSkip skip = Assert.Single(Run().Skipped, s => s.Subject == nameof(ProxyPanel));

        Assert.Contains("PART_Footer", skip.Reason, StringComparison.Ordinal);
        Assert.Null(skip.RelativePath);
    }

    /// <summary>A theme for a type outside the scanned assemblies is not the scan's to explain.</summary>
    [Fact]
    public void AThemeForATypeOutsideTheScan_IsNotListed()
    {
        Write("Other.axaml", Theme("t:SomethingElse", "<Border />"));

        Assert.DoesNotContain(Run().Skipped, s => s.Subject == "SomethingElse");
    }

    /// <summary>A control that was checked is not also listed as skipped.</summary>
    [Fact]
    public void AControlThatWasChecked_IsNotListedAsSkipped()
    {
        Write("Good.axaml",
            Theme("t:ProxyPanel", "<Border Name=\"PART_Header\" /><Border Name=\"PART_Footer\" />"));

        Assert.DoesNotContain(Run().Skipped, s => s.Subject == nameof(ProxyPanel));
    }

    /// <summary>
    /// ⛔ DiffView's catch. A scan copied without <c>WithAssemblies</c> reads no parts, and used to
    /// say only "0 inspected", which reads as success. Every themed control is named as skipped.
    /// </summary>
    [Fact]
    public void WithoutAssemblies_EveryThemedControlIsNamedAsSkipped()
    {
        Write("Bad.axaml", Theme("t:ProxyPanel", "<Border Name=\"PART_Header\" />"));

        XamlRuleResult result = RunWithoutTypes();

        Assert.Empty(result.Findings);
        Assert.Equal(0, result.Inspected);
        XamlSkip skip = Assert.Single(result.Skipped);
        Assert.Equal(nameof(ProxyPanel), skip.Subject);
        Assert.Contains("WithAssemblies", skip.Reason, StringComparison.Ordinal);
        Assert.Equal("Bad.axaml", skip.RelativePath);
    }

    /// <summary>A scan with no themes had nothing to check, so it has nothing to name either.</summary>
    [Fact]
    public void WithoutAssemblies_AndNoThemes_NothingIsSkipped()
    {
        Write("Plain.axaml", $"<UserControl {Header}><Border /></UserControl>");

        Assert.Empty(RunWithoutTypes().Skipped);
    }
}
