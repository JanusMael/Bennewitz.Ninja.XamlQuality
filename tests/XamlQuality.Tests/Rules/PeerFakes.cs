// A miniature framework for CustomControlPeerRuleTests. The rule reads a control's automation peer
// through reflection alone, by the method's name, so these are shaped like one: a root declaration
// that gives no peer, bases that keep it, a base that overrides it, and the controls a consumer
// writes on each. Nothing here is constructed; the rule reads the types, and so do the tests.
namespace XamlQuality.Tests.Rules.PeerFakes
{
    /// <summary>Shaped like Avalonia's <c>Control</c>: where peer creation is declared, giving none.</summary>
    public class FakePeerControl
    {
        /// <summary>The framework's default: no peer a control-view search can reach.</summary>
        /// <returns>Nothing.</returns>
        protected virtual object? OnCreateAutomationPeer() => null;
    }

    /// <summary>Shaped like <c>TemplatedControl</c>: keeps the default.</summary>
    public class FakeTemplatedControl : FakePeerControl;

    /// <summary>Shaped like <c>ContentControl</c>: keeps the default too.</summary>
    public class FakeContentControl : FakeTemplatedControl;

    /// <summary>Shaped like <c>Button</c>: a framework base that gives a peer.</summary>
    public class FakeButton : FakeContentControl
    {
        /// <inheritdoc />
        protected override object? OnCreateAutomationPeer() => new object();
    }

    /// <summary>A consumer's themed control with no peer of its own.</summary>
    public class PeerlessBadge : FakeTemplatedControl;

    /// <summary>The same, on a content base.</summary>
    public class PeerlessPane : FakeContentControl;

    /// <summary>A consumer's control that gives itself a peer.</summary>
    public class PeeredBadge : FakeTemplatedControl
    {
        /// <inheritdoc />
        protected override object? OnCreateAutomationPeer() => new object();
    }

    /// <summary>Derived from a consumer's control that has a peer.</summary>
    public class PeeredBadgeVariant : PeeredBadge;

    /// <summary>Derived from a framework base that has a peer.</summary>
    public class ToolbarButton : FakeButton;

    /// <summary>A decorative control that says so in its own code, by returning the empty peer.</summary>
    public class DecorativeRule : FakeTemplatedControl
    {
        /// <inheritdoc />
        protected override object? OnCreateAutomationPeer() => null;
    }

    /// <summary>A type a theme could name that has no peer creation to read at all.</summary>
    public class NoPeerMethod;

    /// <summary>One of two types a theme cannot tell apart by name.</summary>
    public class Twin : FakeTemplatedControl;
}

namespace XamlQuality.Tests.Rules.PeerFakes.Elsewhere
{
    /// <summary>The other type named <c>Twin</c>.</summary>
    public class Twin : XamlQuality.Tests.Rules.PeerFakes.FakeTemplatedControl;
}
