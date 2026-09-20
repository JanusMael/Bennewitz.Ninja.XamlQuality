namespace Bennewitz.Ninja.XamlQuality;

/// <summary>One rule violation, in one file.</summary>
/// <remarks>
/// <para>
/// ⭐ <b>A finding, not an assertion failure.</b> Rules return these rather than throwing, which
/// is what lets one rule drive an MSTest assertion, an xUnit theory, a CLI report or an MSBuild
/// warning. A library that threw would have chosen the consumer's runner for them, and would have
/// chosen the severity too.
/// </para>
/// <para>
/// ⚠ <b><see cref="Line"/> is nullable on purpose.</b> Some violations are structural rather than
/// positional, and reporting line 1 for those sends the reader somewhere specific and wrong.
/// Absent is honest; an invented number is not.
/// </para>
/// </remarks>
/// <param name="RuleId">The <see cref="IXamlRule.Id"/> that produced this finding.</param>
/// <param name="FilePath">Absolute path to the file the violation is in.</param>
/// <param name="RelativePath">
/// The path as a reader should see it — relative to the scan root and forward-slashed, so a
/// message reads the same on every platform and in CI output.
/// </param>
/// <param name="Line">1-based line number, or <c>null</c> when the violation has no single line.</param>
/// <param name="Message">
/// What is wrong and what to do about it. ⚠ Written for whoever hits it six months from now with
/// no context: name the element and the fix, not just the rule.
/// </param>
public sealed record XamlFinding(
    string RuleId,
    string FilePath,
    string RelativePath,
    int? Line,
    string Message)
{
    /// <summary>A single line suitable for a failure message or a console report.</summary>
    public override string ToString() =>
        Line is { } line
            ? $"{RelativePath}({line}): [{RuleId}] {Message}"
            : $"{RelativePath}: [{RuleId}] {Message}";
}
