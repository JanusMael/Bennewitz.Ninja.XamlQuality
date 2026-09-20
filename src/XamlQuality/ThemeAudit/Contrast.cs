namespace Bennewitz.Ninja.XamlQuality.ThemeAudit;

/// <summary>
/// WCAG 2.x relative luminance and contrast ratio. A foreground token that lands below its floor
/// against a variant's surface is the low-contrast finding; the AA floors are the report's
/// defaults (4.5 for body text, 3.0 for large text and non-text UI such as a change marker or a
/// selection band).
/// </summary>
public static class Contrast
{
    /// <summary>WCAG AA for normal-size text.</summary>
    public const double AaText = 4.5;

    /// <summary>WCAG AA for large text and for non-text UI components and graphics.</summary>
    public const double AaLargeOrGraphic = 3.0;

    /// <summary>
    /// Relative luminance of an opaque colour, per WCAG 2.x. A colour with alpha below 255 must
    /// be composited over its background first (see <see cref="AuditColor.CompositeOver"/>);
    /// this uses the RGB channels as given and ignores alpha.
    /// </summary>
    public static double RelativeLuminance(AuditColor color)
    {
        double r = Linearize(color.R);
        double g = Linearize(color.G);
        double b = Linearize(color.B);
        return 0.2126 * r + 0.7152 * g + 0.0722 * b;
    }

    /// <summary>
    /// Contrast ratio between two colours, 1.0 (identical) to 21.0 (black on white). Order does
    /// not matter. Any alpha below 255 is composited over <paramref name="background"/> first, so
    /// a translucent foreground is scored as seen.
    /// </summary>
    public static double Ratio(AuditColor foreground, AuditColor background)
    {
        AuditColor fg = foreground.A == 255 ? foreground : foreground.CompositeOver(background);
        double l1 = RelativeLuminance(fg);
        double l2 = RelativeLuminance(background);
        double lighter = Math.Max(l1, l2);
        double darker = Math.Min(l1, l2);
        return (lighter + 0.05) / (darker + 0.05);
    }

    private static double Linearize(byte channel)
    {
        double c = channel / 255.0;
        return c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
    }
}
