// Controls that hand their template to other code, for TemplatePartRuleTests. The rule follows a
// template from a control's OnApplyTemplate by name and signature, so these are shaped like a
// framework's: an OnApplyTemplate that is given the template's arguments, whose type declares the
// template's name scope, and a name scope whose lookups start with Find.
namespace XamlQuality.Tests.Rules;

/// <summary>Shaped like a name scope: an instance lookup by name.</summary>
public sealed class HandedNameScope
{
    /// <summary>A lookup by name; only the call's shape matters to the rule.</summary>
    /// <param name="name">The name to find.</param>
    /// <returns>Nothing.</returns>
    public object? Find(string name)
    {
        _ = name;
        return null;
    }
}

/// <summary>Shaped like Avalonia's <c>TemplateAppliedEventArgs</c>: what <c>OnApplyTemplate</c> is given.</summary>
public sealed class HandedTemplateArgs
{
    /// <summary>The applied template's name scope.</summary>
    public HandedNameScope NameScope { get; } = new();
}

/// <summary>Shaped like a templated control, whose <c>OnApplyTemplate</c> is given its template.</summary>
public abstract class HandedTemplatedControl
{
    /// <summary>Called with the control's own template.</summary>
    /// <param name="e">The applied template.</param>
    protected virtual void OnApplyTemplate(HandedTemplateArgs e) => _ = e;
}

/// <summary>Does the part lookups for the controls that hand it their template, as DiffView's <c>DiffBuildController</c> does.</summary>
internal sealed class PartLookupHelper
{
    /// <summary>Finds the parts on the template it is handed.</summary>
    /// <param name="e">A control's applied template.</param>
    public void Apply(HandedTemplateArgs e)
    {
        _ = e.NameScope.Find("PART_Handed");
        _ = e.NameScope.Find("PART_Shared");
    }

    /// <summary>
    /// A handler on another control's template, which no control hands it: its lookup stays the helper's.
    /// </summary>
    /// <param name="sender">The control whose template was applied.</param>
    /// <param name="e">That control's template.</param>
    public void OnOtherTemplateApplied(object? sender, HandedTemplateArgs e)
    {
        _ = sender;
        _ = e.NameScope.Find("PART_Unhanded");
    }
}

/// <summary>Hands its template to the helper, and looks nothing up itself.</summary>
public sealed class HandingHost : HandedTemplatedControl
{
    private readonly PartLookupHelper _helper = new();

    /// <inheritdoc />
    protected override void OnApplyTemplate(HandedTemplateArgs e)
    {
        base.OnApplyTemplate(e);
        _helper.Apply(e);
    }
}

/// <summary>Hands its template to the same helper.</summary>
public sealed class SecondHandingHost : HandedTemplatedControl
{
    private readonly PartLookupHelper _helper = new();

    /// <inheritdoc />
    protected override void OnApplyTemplate(HandedTemplateArgs e) => _helper.Apply(e);
}

/// <summary>Hands on only the template's name scope.</summary>
public sealed class ScopeHandingHost : HandedTemplatedControl
{
    /// <inheritdoc />
    protected override void OnApplyTemplate(HandedTemplateArgs e) => ScopeHelper.Apply(e.NameScope);
}

/// <summary>Finds a part in the name scope it is handed.</summary>
public static class ScopeHelper
{
    /// <summary>Finds the part.</summary>
    /// <param name="scope">A control's template's name scope.</param>
    public static void Apply(HandedNameScope scope) => _ = scope.Find("PART_Scoped");
}

/// <summary>Hands its template down a chain of two helpers.</summary>
public sealed class ChainHost : HandedTemplatedControl
{
    /// <inheritdoc />
    protected override void OnApplyTemplate(HandedTemplateArgs e) => ChainFirst.Apply(e);
}

/// <summary>Passes the template on.</summary>
public static class ChainFirst
{
    /// <summary>Hands the template to the next helper.</summary>
    /// <param name="e">A control's applied template.</param>
    public static void Apply(HandedTemplateArgs e) => ChainSecond.Apply(e);
}

/// <summary>Finds a part at the end of the chain.</summary>
public static class ChainSecond
{
    /// <summary>Finds the part.</summary>
    /// <param name="e">A control's applied template.</param>
    public static void Apply(HandedTemplateArgs e) => _ = e.NameScope.Find("PART_Chained");
}

/// <summary>A base class of the consumer's own that looks its subclasses' parts up.</summary>
public abstract class LookingBase : HandedTemplatedControl
{
    /// <inheritdoc />
    protected override void OnApplyTemplate(HandedTemplateArgs e) => _ = e.NameScope.Find("PART_FromBase");
}

/// <summary>Inherits its base's <c>OnApplyTemplate</c>, which runs on this control's template.</summary>
public sealed class InheritingHost : LookingBase;

/// <summary>Passes its template to a method that takes anything, which is not handing it on.</summary>
public sealed class ObjectHandingHost : HandedTemplatedControl
{
    /// <inheritdoc />
    protected override void OnApplyTemplate(HandedTemplateArgs e) => Describer.Describe(e);
}

/// <summary>Takes any value, and looks a part up on a name scope of its own.</summary>
public static class Describer
{
    /// <summary>Describes a value; the lookup is on a scope it made, not on a template it was handed.</summary>
    /// <param name="value">Anything.</param>
    public static void Describe(object value)
    {
        _ = value;
        _ = new HandedNameScope().Find("PART_Described");
    }
}

/// <summary>Hands its template to a helper's constructor.</summary>
public sealed class ConstructingHost : HandedTemplatedControl
{
    /// <inheritdoc />
    protected override void OnApplyTemplate(HandedTemplateArgs e) => _ = new ConstructedHelper(e);
}

/// <summary>Finds a part while it is constructed with a control's template.</summary>
internal sealed class ConstructedHelper
{
    /// <summary>Finds the part.</summary>
    /// <param name="e">A control's applied template.</param>
    public ConstructedHelper(HandedTemplateArgs e) => _ = e.NameScope.Find("PART_Constructed");
}
