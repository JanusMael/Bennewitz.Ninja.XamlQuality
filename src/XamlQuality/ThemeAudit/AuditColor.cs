using System.Globalization;

namespace Bennewitz.Ninja.XamlQuality.ThemeAudit;

/// <summary>
/// A straight ARGB colour, parsed from the forms a theme dictionary writes: <c>#AARRGGBB</c>,
/// <c>#RRGGBB</c>, <c>#ARGB</c>, <c>#RGB</c>, and the XAML named colours. The tool has no Avalonia
/// dependency, so it parses colours itself rather than through <c>Avalonia.Media.Color</c>.
/// </summary>
public readonly record struct AuditColor(byte A, byte R, byte G, byte B)
{
    /// <summary>Fully transparent black — the "resolves to nothing" colour a missing brush paints.</summary>
    public static AuditColor Transparent => new(0, 0, 0, 0);

    /// <summary>Alpha as a 0..1 fraction.</summary>
    public double Alpha => A / 255.0;

    /// <summary>
    /// Parses <paramref name="text"/> as a hex colour (<c>#AARRGGBB</c>, <c>#RRGGBB</c>,
    /// <c>#ARGB</c> or <c>#RGB</c>) or a XAML named colour, case-insensitively. Returns false
    /// for anything else, including a <c>{StaticResource ...}</c> reference or a gradient — the
    /// caller records those as unresolved rather than treating them as a colour.
    /// </summary>
    public static bool TryParse(string? text, out AuditColor color)
    {
        color = default;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        string value = text.Trim();
        if (value[0] == '#')
        {
            return TryParseHex(value.AsSpan(1), out color);
        }

        return NamedColors.Table.TryGetValue(value, out color);
    }

    private static bool TryParseHex(ReadOnlySpan<char> hex, out AuditColor color)
    {
        color = default;
        switch (hex.Length)
        {
            case 8: // AARRGGBB
                return TryByte(hex[..2], out byte a8) && TryByte(hex[2..4], out byte r8)
                       && TryByte(hex[4..6], out byte g8) && TryByte(hex[6..8], out byte b8)
                       && Set(a8, r8, g8, b8, out color);
            case 6: // RRGGBB
                return TryByte(hex[..2], out byte r6) && TryByte(hex[2..4], out byte g6)
                       && TryByte(hex[4..6], out byte b6)
                       && Set(255, r6, g6, b6, out color);
            case 4: // ARGB, each nibble doubled
                return TryNibble(hex[0], out byte a4) && TryNibble(hex[1], out byte r4)
                       && TryNibble(hex[2], out byte g4) && TryNibble(hex[3], out byte b4)
                       && Set(a4, r4, g4, b4, out color);
            case 3: // RGB, each nibble doubled
                return TryNibble(hex[0], out byte r3) && TryNibble(hex[1], out byte g3)
                       && TryNibble(hex[2], out byte b3)
                       && Set(255, r3, g3, b3, out color);
            default:
                return false;
        }
    }

    private static bool Set(byte a, byte r, byte g, byte b, out AuditColor color)
    {
        color = new AuditColor(a, r, g, b);
        return true;
    }

    private static bool TryByte(ReadOnlySpan<char> pair, out byte value)
    {
        return byte.TryParse(pair, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value);
    }

    private static bool TryNibble(char c, out byte value)
    {
        if (byte.TryParse([c], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte nibble))
        {
            value = (byte)(nibble << 4 | nibble);
            return true;
        }

        value = 0;
        return false;
    }

    /// <summary>
    /// Composites this colour over an opaque <paramref name="background"/> using this colour's
    /// alpha, so a semi-transparent token is scored as the reader actually sees it. The result is
    /// opaque; the background is assumed opaque (a variant's page surface always is).
    /// </summary>
    public AuditColor CompositeOver(AuditColor background)
    {
        double alpha = Alpha;
        byte Blend(byte fg, byte bg) => (byte)Math.Round(fg * alpha + bg * (1 - alpha));
        return new AuditColor(255, Blend(R, background.R), Blend(G, background.G), Blend(B, background.B));
    }

    /// <inheritdoc/>
    public override string ToString()
    {
        return $"#{A:X2}{R:X2}{G:X2}{B:X2}";
    }
}
