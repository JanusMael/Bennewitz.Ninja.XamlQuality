using System.Collections.Concurrent;
using Avalonia.Automation.Peers;

// A miniature framework for KeyBindingFocusRuleTests and ItemContainerNameRuleTests. Both rules read a
// framework through reflection alone, so these are shaped like one: a FocusableProperty whose
// per-type defaults are registered by static constructors, items controls that construct their
// containers, and containers that construct their automation peers, which are in ContainerNameFakes.cs.
//
// ⛔ Never instantiate these in a test. Construction runs the static constructors, and the rule has
// to run them itself: a read made before they run reports every control unfocusable, which is the
// trap the rule exists to get right, and a test that ran them first would hide it.
namespace XamlQuality.Tests.Rules.FocusFakes;

/// <summary>Shaped like a styled property: per-type defaults, looked up along the base chain.</summary>
public sealed class FakeFocusableProperty
{
    private readonly ConcurrentDictionary<Type, bool> _defaults = new();

    /// <summary>Registers a type's default, as a static constructor does.</summary>
    /// <param name="type">The type the default is for.</param>
    /// <param name="value">The default.</param>
    public void OverrideDefaultValue(Type type, bool value) => _defaults[type] = value;

    /// <summary>The metadata for <paramref name="type"/>, as registered so far.</summary>
    /// <param name="type">The type asked about.</param>
    /// <returns>Its metadata; the base default when nothing in its chain has registered one yet.</returns>
    public FakeMetadata GetMetadata(Type type)
    {
        for (Type? link = type; link is not null; link = link.BaseType)
        {
            if (_defaults.TryGetValue(link, out bool value))
            {
                return new FakeMetadata(value);
            }
        }

        return new FakeMetadata(false);
    }
}

/// <summary>Shaped like property metadata: a default value.</summary>
/// <param name="defaultValue">The default it carries.</param>
public sealed class FakeMetadata(bool defaultValue)
{
    /// <summary>The default.</summary>
    public bool DefaultValue { get; } = defaultValue;
}

/// <summary>Where the focus model is declared.</summary>
public class InputElement
{
    /// <summary>Found by name, as the rule finds a real framework's.</summary>
    public static readonly FakeFocusableProperty FocusableProperty = new();
}

/// <summary>A control that cannot take focus.</summary>
public class Control : InputElement
{
    /// <summary>Shaped like the framework's: a peer no control-view search reaches.</summary>
    /// <returns>The peer.</returns>
    protected virtual AutomationPeer OnCreateAutomationPeer() => new NoneAutomationPeer(this);
}

/// <summary>A control drawn through a template it is given.</summary>
public class TemplatedControl : Control
{
    /// <summary>Its template; only the property's existence matters.</summary>
    public object? Template { get; set; }

    /// <summary>The key its implicit theme is found under, shaped like the framework's.</summary>
    protected virtual Type StyleKeyOverride => GetType();
}

/// <summary>The container an <see cref="ItemsControl"/> generates: not templated, and unfocusable.</summary>
public class ContentPresenter : Control;

/// <summary>Presents items in containers it constructs.</summary>
public class ItemsControl : TemplatedControl
{
    /// <summary>Shaped like the framework's container factory.</summary>
    /// <returns>A new container.</returns>
    protected virtual Control CreateContainerForItemOverride() => new ContentPresenter();
}

/// <summary>A list's row: focusable by default, once its static constructor has run, and a list item to automation.</summary>
public class ListBoxItem : TemplatedControl
{
    static ListBoxItem() => FocusableProperty.OverrideDefaultValue(typeof(ListBoxItem), true);

    /// <inheritdoc />
    protected override AutomationPeer OnCreateAutomationPeer() => new ListItemAutomationPeer(this);
}

/// <summary>A list: unfocusable itself, with focusable rows.</summary>
public class ListBox : ItemsControl
{
    /// <inheritdoc />
    protected override Control CreateContainerForItemOverride() => new ListBoxItem();
}

/// <summary>A drop-down's row, which keeps its base's peer.</summary>
public class ComboBoxItem : ListBoxItem;

/// <summary>A drop-down list.</summary>
public class ComboBox : ItemsControl
{
    /// <inheritdoc />
    protected override Control CreateContainerForItemOverride() => new ComboBoxItem();
}

/// <summary>A custom list that borrows its base's theme key, as TailBlazer's <c>LinesListBox</c> does.</summary>
public class BorrowingListBox : ListBox
{
    /// <inheritdoc />
    protected override Type StyleKeyOverride => typeof(ListBox);
}

/// <summary>A tree's row, which presents items of its own.</summary>
public class TreeViewItem : ItemsControl
{
    static TreeViewItem() => FocusableProperty.OverrideDefaultValue(typeof(TreeViewItem), true);

    /// <inheritdoc />
    protected override AutomationPeer OnCreateAutomationPeer() => new TreeViewItemAutomationPeer(this);
}

/// <summary>A tree: unfocusable itself, with focusable rows.</summary>
public class TreeView : ItemsControl
{
    /// <inheritdoc />
    protected override Control CreateContainerForItemOverride() => new TreeViewItem();
}

/// <summary>A tab, which is a list item to automation.</summary>
public class TabItem : TemplatedControl
{
    static TabItem() => FocusableProperty.OverrideDefaultValue(typeof(TabItem), true);

    /// <inheritdoc />
    protected override AutomationPeer OnCreateAutomationPeer() => new ListItemAutomationPeer(this);
}

/// <summary>Presents its selected item's content as well as its items.</summary>
public class TabControl : ItemsControl
{
    /// <summary>The template the selected content is shown with.</summary>
    public object? ContentTemplate { get; set; }

    /// <inheritdoc />
    protected override Control CreateContainerForItemOverride() => new TabItem();
}

/// <summary>Text: unfocusable, and not templated.</summary>
public class TextBlock : Control;

/// <summary>A decorator: unfocusable, and not templated.</summary>
public class Border : Control;

/// <summary>A panel: unfocusable, and not templated.</summary>
public class StackPanel : Control;

/// <summary>A text input: focusable.</summary>
public class TextBox : TemplatedControl
{
    static TextBox() => FocusableProperty.OverrideDefaultValue(typeof(TextBox), true);
}

/// <summary>A button: focusable.</summary>
public class Button : TemplatedControl
{
    static Button() => FocusableProperty.OverrideDefaultValue(typeof(Button), true);
}

/// <summary>Unfocusable itself, with a template that may hold something focusable.</summary>
public class Expander : TemplatedControl;

/// <summary>A view: unfocusable, not an items control.</summary>
public class UserControl : TemplatedControl;
