using System.Reflection;

namespace Bennewitz.Ninja.XamlQuality.Rules;

/// <summary>
/// Every custom control with a theme gets an automation peer, from its own code or from a framework
/// base that gives one, or declares in its own code that it has none.
/// </summary>
/// <remarks>
/// <para>
/// ⭐ <b>A control with no peer is invisible to a control-view search, however it is named.</b> The
/// framework's default <c>OnCreateAutomationPeer</c> gives a control an empty peer: Avalonia's
/// <c>Control</c> returns a <c>NoneAutomationPeer</c>, which is not a control element, so a search of
/// the control view never finds the control, its <c>AutomationId</c> or its <c>Name</c>. A name gate
/// such as <c>BNXQ1001</c> stays green over a control nobody can reach. Measured on Avalonia 12.1.3,
/// headless: a <c>TemplatedControl</c> subclass carrying an <c>AutomationId</c> was in the peer tree
/// with <c>IsControlElement</c> false, and the same control with a peer of its own was a control
/// element. Content inside a peer-less control stays reachable; the control itself does not.
/// </para>
/// <para>
/// ⚠ <b>Most of the bases a custom control starts from give it no peer.</b> Measured on Avalonia
/// 12.1.3 by subclassing every public control it exports: 59 of 123 give a subclass no peer, among them
/// <c>TemplatedControl</c>, <c>ContentControl</c>, <c>HeaderedContentControl</c>, <c>Decorator</c>,
/// <c>Border</c>, every panel, <c>SplitView</c> and <c>TransitioningContentControl</c>.
/// </para>
/// <para>
/// ⭐ <b>Read from the type, without running it.</b> A control has a peer when the
/// <c>OnCreateAutomationPeer</c> its type resolves to is an override, anywhere in its chain, and not
/// the root declaration. The method is found by name, so no framework assembly is referenced. Measured
/// against those 123 subclasses, this reading agreed with the peer each one really got on 122. The one
/// exception is
/// <c>AccessText</c>, whose own override returns an empty peer, so a control derived from it is taken
/// to have a peer.
/// </para>
/// <para>
/// ⭐ <b>A decorative control says so in its own code.</b> Any override counts, including one that
/// returns the empty peer on purpose, and that override is the opt-out: it puts the decision beside the
/// control, where whoever reads the control sees it. Custom drawing alone does not make a control
/// decorative, and a list of exempt names kept in a test would drift from the controls it names.
/// </para>
/// <para>
/// ⚠ <b>The controls checked are the ones the scanned markup themes</b>, through a
/// <c>ControlTheme</c> whose target the scanned assemblies define, matched by name as <c>BNXQ1003</c>
/// matches them. A theme for a type the scan was not given is a framework's or another package's, and
/// not the scan's to explain. What the rule sees but cannot read is named in
/// <see cref="XamlRuleResult.Skipped"/>: every themed control when the scan was given no assemblies, a
/// themed control that did not load, a name more than one scanned type carries, and a type with no
/// <c>OnCreateAutomationPeer</c> to read.
/// </para>
/// </remarks>
public sealed class CustomControlPeerRule : IXamlRule
{
    private const string PeerMethod = "OnCreateAutomationPeer";

    /// <inheritdoc />
    public string Id => "BNXQ1006";

    /// <inheritdoc />
    public string Summary => "Every themed custom control gets an automation peer, or declares in its own code that it has none.";

    /// <inheritdoc />
    public XamlRuleResult Analyze(XamlScanContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        // One entry per control, at its first theme: a control themed twice is still one control.
        (XamlFile File, string Target, int? Line)[] themed =
        [
            .. ControlThemes.In(context)
                .DistinctBy(theme => theme.Target, StringComparer.Ordinal)
                .Select(theme => (theme.File, theme.Target, theme.Line)),
        ];

        if (context.Assemblies.Count == 0)
        {
            // Nothing to read peers from. Every themed control is named, so a consumer who forgot
            // WithAssemblies is told so, not reassured.
            return XamlRuleResult.Clean(0) with
            {
                Skipped =
                [
                    .. themed.Select(theme => new XamlSkip(
                        theme.Target,
                        "The scan was given no assemblies, so whether this control has an automation peer could not "
                        + "be read and nothing was checked. Call WithAssemblies with the assembly that defines it.",
                        theme.File.RelativePath,
                        theme.Line)),
                ],
            };
        }

        Dictionary<string, List<Type>> defined = new(StringComparer.Ordinal);
        Dictionary<string, (string Source, string Cause)> notLoaded = new(StringComparer.Ordinal);
        foreach (Assembly assembly in context.Assemblies)
        {
            LoadedTypes types = LoadedTypes.Of(assembly);
            foreach ((string name, string cause) in types.NotLoaded)
            {
                notLoaded.TryAdd(name, (types.Source, cause));
            }

            foreach (Type type in types.Loaded)
            {
                if (LoadedTypes.TopLevelName(type) is not { } name)
                {
                    continue;
                }

                if (!defined.TryGetValue(name, out List<Type>? named))
                {
                    named = [];
                    defined[name] = named;
                }

                named.Add(type);
            }
        }

        List<XamlFinding> findings = [];
        List<XamlSkip> skipped = [];
        int inspected = 0;

        foreach ((XamlFile file, string target, int? line) in themed)
        {
            if (!defined.TryGetValue(target, out List<Type>? candidates))
            {
                if (notLoaded.TryGetValue(target, out (string Source, string Cause) unloaded))
                {
                    // Not a framework control: the scan was given the assembly that defines it.
                    skipped.Add(new XamlSkip(
                        target,
                        $"It has a ControlTheme here, and {unloaded.Source}, which the scan was given, defines it, "
                        + $"but it could not be loaded: {unloaded.Cause}. Whether it has an automation peer was not "
                        + $"checked. {LoadedTypes.Remedy}",
                        file.RelativePath,
                        line));
                }

                // Any other theme is for a type the scanned assemblies do not define, usually a
                // framework control's, which is not the scan's to explain.
                continue;
            }

            if (candidates.Count > 1)
            {
                skipped.Add(new XamlSkip(
                    target,
                    $"More than one type the scan was given is named {target}: "
                    + $"{LoadedTypes.List(candidates.Select(candidate => candidate.FullName ?? candidate.Name))}. "
                    + "A ControlTheme names its target without the namespace this rule reads, so which one it "
                    + "themes is not known, and neither was checked.",
                    file.RelativePath,
                    line));
                continue;
            }

            (string? Root, bool Overridden, string? Base) peer;
            try
            {
                peer = PeerOf(candidates[0]);
            }
            catch (Exception ex) when (ElementTypes.IsUnreadable(ex))
            {
                skipped.Add(new XamlSkip(
                    target,
                    $"Its chain could not be read: {LoadedTypes.CauseOf([ex])}. Whether it has an automation peer "
                    + $"was not checked. {LoadedTypes.Remedy}",
                    file.RelativePath,
                    line));
                continue;
            }

            if (peer.Root is null)
            {
                skipped.Add(new XamlSkip(
                    target,
                    $"Nothing in its chain declares {PeerMethod}, so this rule cannot tell whether it has an "
                    + "automation peer. It reads a framework that creates peers through that method, as Avalonia does.",
                    file.RelativePath,
                    line));
                continue;
            }

            inspected++;
            if (peer.Overridden)
            {
                // An override somewhere in its chain: its own peer, its base's, or a declared empty one.
                continue;
            }

            findings.Add(new XamlFinding(
                Id,
                file.Path,
                file.RelativePath,
                line,
                $"{target} derives from {peer.Base}, and nothing between it and {peer.Root} overrides {PeerMethod}, "
                + $"so it keeps {peer.Root}'s default: no automation peer that a control-view search can reach, and so "
                + $"no AutomationId or Name anyone can find. Override {PeerMethod} to return a peer named for what "
                + "the control shows. If the control is decorative, override it to return the empty peer, "
                + "NoneAutomationPeer in Avalonia, which says so beside the control."));
        }

        return new XamlRuleResult(findings, inspected) { Skipped = skipped };
    }

    /// <summary>
    /// How <paramref name="control"/> resolves <c>OnCreateAutomationPeer</c>: the name of the type that
    /// declares it first, or <c>null</c> when nothing in its chain does; whether a type below that one
    /// overrides it; and the name of the control's base type.
    /// </summary>
    /// <remarks>
    /// ⚠ Reflection hands back the most-derived override of a virtual, so the method it resolves is the
    /// root declaration exactly when nothing in the chain overrides it. A method that hides the root with
    /// <c>new</c> resolves to itself as its own root, and is reported, which is right: the framework still
    /// calls the root.
    /// </remarks>
    private static (string? Root, bool Overridden, string? Base) PeerOf(Type control)
    {
        MethodInfo? resolved = control.GetMethod(
            PeerMethod,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            Type.EmptyTypes,
            modifiers: null);
        string? derivesFrom = control.BaseType?.Name;
        if (resolved is null)
        {
            return (null, false, derivesFrom);
        }

        Type? root = resolved.GetBaseDefinition().DeclaringType;
        return (root?.Name, resolved.DeclaringType != root, derivesFrom);
    }
}
