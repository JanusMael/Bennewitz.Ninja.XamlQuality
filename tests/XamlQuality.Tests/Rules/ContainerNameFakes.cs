// What ItemContainerNameRuleTests read beside FocusFakes.cs: the automation peers its containers
// construct, the item types its templates declare, and a list whose rows name themselves. Nothing here
// is constructed; the rule reads the types, and so do the tests.

// The peers are in Avalonia's own namespace, because the rule recognises a list or tree row by its
// peer's full name. They are shaped like 12.1.3's: a list row's peer takes its name from the content
// control peer's GetNameCore, and a tree row's from the control peer's.
namespace Avalonia.Automation.Peers
{
    /// <summary>Where naming is declared.</summary>
    public abstract class AutomationPeer
    {
        /// <summary>The name, as the peer computes it.</summary>
        /// <returns>The name, or <c>null</c>.</returns>
        protected abstract string? GetNameCore();
    }

    /// <summary>A control's peer: named by <c>AutomationProperties</c> alone.</summary>
    /// <param name="owner">The control.</param>
    public class ControlAutomationPeer(object owner) : AutomationPeer
    {
        /// <summary>The control.</summary>
        public object Owner { get; } = owner;

        /// <inheritdoc />
        protected override string? GetNameCore() => null;
    }

    /// <summary>The peer a control that makes none gets.</summary>
    /// <param name="owner">The control.</param>
    public class NoneAutomationPeer(object owner) : ControlAutomationPeer(owner);

    /// <summary>A content control's peer: its own name, then what it presents, then its content's <c>ToString()</c>.</summary>
    /// <param name="owner">The control.</param>
    public class ContentControlAutomationPeer(object owner) : ControlAutomationPeer(owner)
    {
        /// <inheritdoc />
        protected override string? GetNameCore() => base.GetNameCore();
    }

    /// <summary>A list or tab row's peer, which keeps the content control's naming.</summary>
    /// <param name="owner">The row.</param>
    public class ListItemAutomationPeer(object owner) : ContentControlAutomationPeer(owner);

    /// <summary>An items control's peer.</summary>
    /// <param name="owner">The control.</param>
    public class ItemsControlAutomationPeer(object owner) : ControlAutomationPeer(owner);

    /// <summary>A tree row's peer, which keeps the control peer's naming.</summary>
    /// <param name="owner">The row.</param>
    public class TreeViewItemAutomationPeer(object owner) : ItemsControlAutomationPeer(owner);
}

namespace XamlQuality.Tests.Rules.ItemFakes
{
    using Avalonia.Automation.Peers;
    using XamlQuality.Tests.Rules.FocusFakes;

    /// <summary>A view model that writes no <c>ToString()</c>.</summary>
    public sealed class PlainItem
    {
        /// <summary>What a row shows.</summary>
        public string Label { get; } = string.Empty;
    }

    /// <summary>A view model that writes its own <c>ToString()</c>.</summary>
    public sealed class NamedItem
    {
        /// <summary>What a row shows.</summary>
        public string Label { get; } = string.Empty;

        /// <inheritdoc />
        public override string ToString() => Label;
    }

    /// <summary>A record, whose <c>ToString()</c> the compiler writes.</summary>
    /// <param name="Label">What a row shows.</param>
    public sealed record RecordItem(string Label);

    /// <summary>A record that writes its own <c>ToString()</c>.</summary>
    /// <param name="Label">What a row shows.</param>
    public sealed record NamedRecordItem(string Label)
    {
        /// <inheritdoc />
        public override string ToString() => Label;
    }

    /// <summary>A record struct, whose <c>ToString()</c> the compiler writes too.</summary>
    /// <param name="Label">What a row shows.</param>
    public readonly record struct RecordStructItem(string Label);

    /// <summary>A struct that writes no <c>ToString()</c>, so <c>ValueType</c>'s names it.</summary>
    public readonly struct StructItem
    {
        /// <summary>What a row shows.</summary>
        public int Value { get; }
    }

    /// <summary>An enum, named by its member.</summary>
    public enum ModeItem
    {
        /// <summary>One.</summary>
        First,

        /// <summary>Another.</summary>
        Second,
    }

    /// <summary>A base the items' concrete types derive from.</summary>
    public abstract class AbstractItem;

    /// <summary>What the items' concrete types implement.</summary>
    public interface IItem;

    /// <summary>A type that writes no <c>ToString()</c>, with a subclass that does.</summary>
    public class BaseItem;

    /// <summary>The subclass that writes one.</summary>
    public sealed class DerivedItem : BaseItem
    {
        /// <inheritdoc />
        public override string ToString() => nameof(DerivedItem);
    }

    /// <summary>An unsealed type that writes no <c>ToString()</c>, and nothing derives from it.</summary>
    public class OpenItem;

    /// <summary>A control used as an item.</summary>
    public class ControlItem : Control;

    /// <summary>A consumer's row peer that computes its own name.</summary>
    /// <param name="owner">The row.</param>
    public sealed class SelfNamingPeer(object owner) : ListItemAutomationPeer(owner)
    {
        /// <inheritdoc />
        protected override string? GetNameCore() => "row";
    }

    /// <summary>A consumer's row that creates that peer.</summary>
    public class SelfNamingRow : ListBoxItem
    {
        /// <inheritdoc />
        protected override AutomationPeer OnCreateAutomationPeer() => new SelfNamingPeer(this);
    }

    /// <summary>A consumer's list whose rows name themselves.</summary>
    public class SelfNamingList : ListBox
    {
        /// <inheritdoc />
        protected override Control CreateContainerForItemOverride() => new SelfNamingRow();
    }
}

namespace XamlQuality.Tests.Rules.ItemFakes.Elsewhere
{
    /// <summary>Another <c>PlainItem</c>, in another namespace, that writes its own <c>ToString()</c>.</summary>
    public sealed class PlainItem
    {
        /// <summary>What a row shows.</summary>
        public string Label { get; } = string.Empty;

        /// <inheritdoc />
        public override string ToString() => Label;
    }
}
