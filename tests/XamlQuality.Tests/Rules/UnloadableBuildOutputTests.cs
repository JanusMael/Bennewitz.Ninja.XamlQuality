using Bennewitz.Ninja.XamlQuality;
using Bennewitz.Ninja.XamlQuality.Rules;

namespace XamlQuality.Tests.Rules;

/// <summary>
/// The rules that read compiled code, handed build output whose dependency does not load.
/// </summary>
/// <remarks>
/// ⭐ <b>A rule returns findings; it never throws.</b> Before this, <see cref="TemplatePartRule"/>
/// threw <c>FileNotFoundException</c> from the first method body that named a type it could not load,
/// and <see cref="KeyBindingFocusRule"/> told the consumer to pass the assembly they had passed.
/// <see cref="CustomControlPeerRule"/> reads compiled code too, and is held to the same. The fixture
/// is <see cref="UnloadableAssembly"/>.
/// </remarks>
public sealed class UnloadableBuildOutputTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("xq-").FullName;

    public void Dispose()
    {
        if (Directory.Exists(_root)) { Directory.Delete(_root, recursive: true); }
    }

    private XamlScanContext Scan(string markup)
    {
        File.WriteAllText(Path.Combine(_root, "View.axaml"), markup);
        return XamlScanContext.Load(_root).WithAssemblies([UnloadableAssembly.Consumer]);
    }

    private static string Themes(params string[] targets) =>
        "<ResourceDictionary xmlns=\"https://github.com/avaloniaui\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\">"
        + string.Concat(targets.Select(target =>
            $"<ControlTheme x:Key=\"{target}Theme\" TargetType=\"{target}\"><Setter Property=\"Template\"><ControlTemplate>"
            + $"<Border Name=\"PART_{(target == "Thing" ? "Knob" : target)}\" /></ControlTemplate></Setter></ControlTheme>"))
        + "</ResourceDictionary>";

    private static string KeyBindingOn(string host) =>
        $"<UserControl xmlns=\"https://github.com/avaloniaui\"><{host}><{host}.KeyBindings>"
        + $"<KeyBinding Gesture=\"Ctrl+K\" /></{host}.KeyBindings></{host}></UserControl>";

    /// <summary>
    /// ⛔ XQ1003's control whose base type does not load is named with what stopped it. Read as a
    /// framework control, as it was, its theme was passed over in silence.
    /// </summary>
    [Fact]
    public void XQ1003_AControlThatDoesNotLoad_IsNamedWithWhatStoppedIt()
    {
        XamlRuleResult result = new TemplatePartRule().Analyze(Scan(Themes("Thing")));

        Assert.Empty(result.Findings);
        Assert.Equal(0, result.Inspected);
        XamlSkip skip = Assert.Single(result.Skipped, entry => entry.Subject == "Thing");
        Assert.Contains("Consumer, which the scan was given, defines it, but it could not be loaded", skip.Reason, StringComparison.Ordinal);
        Assert.Contains("Dep could not be found", skip.Reason, StringComparison.Ordinal);
    }

    /// <summary>
    /// ⛔ A method whose body names a type that does not load costs only itself. The constant is
    /// still read and checked, and the control is named for the code that could not be.
    /// </summary>
    [Fact]
    public void XQ1003_AMethodWhoseLocalsDoNotLoad_CostsOnlyItself()
    {
        XamlRuleResult result = new TemplatePartRule().Analyze(Scan(Themes("Plain")));

        Assert.Empty(result.Findings);
        Assert.Equal(1, result.Inspected);
        XamlSkip skip = Assert.Single(result.Skipped);
        Assert.Equal("Plain", skip.Subject);
        Assert.Contains("Part of its code could not be read", skip.Reason, StringComparison.Ordinal);
        Assert.Contains("Dep could not be found", skip.Reason, StringComparison.Ordinal);
    }

    /// <summary>
    /// ⛔ A nested type that loads while the type enclosing it does not, as a lambda's closure does,
    /// is not a reason to throw, nor a control to report. The unthemed <c>Plain</c> is named, which
    /// shows the assembly's types were read.
    /// </summary>
    [Fact]
    public void XQ1003_ANestedTypeWhoseOwnerDoesNotLoad_DoesNotThrow()
    {
        XamlRuleResult result = new TemplatePartRule().Analyze(Scan(Themes()));

        Assert.Empty(result.Findings);
        Assert.Contains(result.Skipped, skip => skip.Subject == "Plain");
        Assert.DoesNotContain(result.Skipped, skip => skip.Subject is "Thing" or "Closure");
    }

    /// <summary>⚠ A theme for a type the scanned assemblies do not define is still not the scan's to explain.</summary>
    [Fact]
    public void XQ1003_AFrameworkControlsTheme_IsStillNotExplained()
    {
        XamlRuleResult result = new TemplatePartRule().Analyze(Scan(Themes("Button")));

        Assert.Contains(result.Skipped, skip => skip.Subject == "Plain");
        Assert.DoesNotContain(result.Skipped, skip => skip.Subject == "Button");
    }

    /// <summary>
    /// ⛔ XQ1006's themed control that does not load is named with what stopped it: neither reported,
    /// which would blame a peer nobody read, nor passed over as a framework control's theme.
    /// </summary>
    [Fact]
    public void XQ1006_AControlThatDoesNotLoad_IsNamedWithWhatStoppedIt()
    {
        XamlRuleResult result = new CustomControlPeerRule().Analyze(Scan(Themes("Thing")));

        Assert.Empty(result.Findings);
        Assert.Equal(0, result.Inspected);
        XamlSkip skip = Assert.Single(result.Skipped);
        Assert.Equal("Thing", skip.Subject);
        Assert.Contains("Consumer, which the scan was given, defines it, but it could not be loaded", skip.Reason, StringComparison.Ordinal);
        Assert.Contains("Dep could not be found", skip.Reason, StringComparison.Ordinal);
    }

    /// <summary>
    /// ⛔ XQ1006 never throws on a control that loads but whose peer method cannot be resolved, here
    /// for an overload whose parameter's type does not load. The control is named with what stopped it.
    /// </summary>
    [Fact]
    public void XQ1006_APeerMethodThatCannotBeRead_IsNamedNotThrown()
    {
        XamlRuleResult result = new CustomControlPeerRule().Analyze(Scan(Themes("Overloaded")));

        Assert.Empty(result.Findings);
        Assert.Equal(0, result.Inspected);
        XamlSkip skip = Assert.Single(result.Skipped);
        Assert.Equal("Overloaded", skip.Subject);
        Assert.Contains("Its chain could not be read: Dep could not be found", skip.Reason, StringComparison.Ordinal);
    }

    /// <summary>
    /// ⛔ XQ1005 says the control is there and did not load. It said the control was not in the
    /// assemblies the scan was given, and told the consumer to pass the one they had passed.
    /// </summary>
    [Fact]
    public void XQ1005_AControlThatDoesNotLoad_SaysSo_NotThatItWasNotPassed()
    {
        XamlRuleResult result = new KeyBindingFocusRule().Analyze(Scan(KeyBindingOn("Thing")));

        XamlSkip skip = Assert.Single(result.Skipped);
        Assert.Contains("Thing, defined in Consumer, could not be loaded: Dep could not be found", skip.Reason, StringComparison.Ordinal);
        Assert.DoesNotContain("Pass the assembly that defines it", skip.Reason, StringComparison.Ordinal);
    }

    /// <summary>⚠ A type in an assembly the scanned ones reference, which did not load, is named as that.</summary>
    [Fact]
    public void XQ1005_AReferencedAssemblyThatDoesNotLoad_IsNamed()
    {
        XamlRuleResult result = new KeyBindingFocusRule().Analyze(Scan(KeyBindingOn("DepBase")));

        XamlSkip skip = Assert.Single(result.Skipped);
        Assert.Contains("DepBase is not a type in the scanned assemblies, and Dep, which they reference, could not be loaded", skip.Reason, StringComparison.Ordinal);
    }
}
