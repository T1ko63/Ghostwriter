using System.Globalization;
using System.Text.RegularExpressions;

namespace Kuroko.Core.Config;

/// <summary>A colour with alpha, UI-framework free. Alpha 255 is opaque.</summary>
public readonly record struct Rgba(byte A, byte R, byte G, byte B)
{
    public static Rgba Opaque(byte r, byte g, byte b) => new(255, r, g, b);

    public Rgba WithAlpha(byte alpha) => this with { A = alpha };

    /// <summary>Same colour with its alpha multiplied by <paramref name="factor"/> (0..1).</summary>
    public Rgba Scaled(double factor) => this with { A = (byte)Math.Clamp((int)Math.Round(A * factor, MidpointRounding.AwayFromZero), 0, 255) };

    /// <summary>This colour laid over an opaque <paramref name="backdrop"/>: the result is opaque.</summary>
    public Rgba Over(Rgba backdrop)
    {
        var a = A / 255.0;
        byte Mix(byte top, byte bottom) => (byte)Math.Round(top * a + bottom * (1 - a), MidpointRounding.AwayFromZero);
        return Opaque(Mix(R, backdrop.R), Mix(G, backdrop.G), Mix(B, backdrop.B));
    }

    /// <summary>Perceived brightness 0..1 (Rec. 709 weights, no gamma handling: good enough to tell dark from light).</summary>
    public double Luminance => (0.2126 * R + 0.7152 * G + 0.0722 * B) / 255.0;

    public override string ToString() => $"#{A:X2}{R:X2}{G:X2}{B:X2}";
}

/// <summary>
/// Parses the colour formats of the appearance settings: <c>#RRGGBB</c>, <c>#AARRGGBB</c> (alpha first, as in
/// Windows/WPF, not the CSS order <c>#RRGGBBAA</c>), and the CSS functions <c>rgb(r, g, b)</c> and
/// <c>rgba(r, g, b, a)</c> with a from 0 to 1 (or a percentage). The functions are what colour pickers in editors
/// write, and their alpha cannot be mistaken for the other hex order. Upper and lower case are both fine.
/// </summary>
public static class ColorParser
{
    private static readonly Regex Function = new(
        @"^rgba?\(\s*(\d{1,3})\s*,\s*(\d{1,3})\s*,\s*(\d{1,3})\s*(?:,\s*(\d*\.?\d+%?)\s*)?\)$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <param name="hasAlpha">True when the text carried an alpha part (<c>#AARRGGBB</c> or a fourth <c>rgba</c> value); it is then in <paramref name="color"/>.</param>
    public static bool TryParse(string? text, out Rgba color, out bool hasAlpha)
    {
        color = default;
        hasAlpha = false;
        if (text is null) return false;

        var trimmed = text.Trim();
        return trimmed.StartsWith('#') ? TryParseHex(trimmed, out color, out hasAlpha) : TryParseFunction(trimmed, out color, out hasAlpha);
    }

    /// <summary>
    /// The hex forms of the <c>accent</c> setting: <c>#RGB</c>, <c>#RRGGBB</c> or <c>#AARRGGBB</c>. The result is always
    /// opaque: an alpha part is accepted but ignored, because the accent tints bring their own transparency.
    /// </summary>
    public static bool TryParseAccentHex(string text, out Rgba color)
    {
        color = default;
        if (!text.StartsWith('#')) return false;
        var hex = text[1..];
        if (hex.Length is not (3 or 6 or 8) || !hex.All(Uri.IsHexDigit)) return false;
        if (hex.Length == 3) hex = string.Concat(hex.Select(ch => new string(ch, 2)));
        if (hex.Length == 8) hex = hex[2..];

        var rgb = uint.Parse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        color = Rgba.Opaque((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
        return true;
    }

    private static bool TryParseHex(string text, out Rgba color, out bool hasAlpha)
    {
        color = default;
        hasAlpha = false;
        if (text.Length is not (7 or 9)) return false;
        var hex = text[1..];
        if (!hex.All(Uri.IsHexDigit)) return false;

        var value = uint.Parse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        if (hex.Length == 8)
        {
            hasAlpha = true;
            color = new Rgba((byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value);
        }
        else
        {
            color = Rgba.Opaque((byte)(value >> 16), (byte)(value >> 8), (byte)value);
        }

        return true;
    }

    private static bool TryParseFunction(string text, out Rgba color, out bool hasAlpha)
    {
        color = default;
        hasAlpha = false;
        var match = Function.Match(text);
        if (!match.Success) return false;

        var channels = new int[3];
        for (var i = 0; i < 3; i++)
        {
            channels[i] = int.Parse(match.Groups[i + 1].Value, CultureInfo.InvariantCulture);
            if (channels[i] > 255) return false;
        }

        var alpha = (byte)255;
        if (match.Groups[4].Success)
        {
            var part = match.Groups[4].Value;
            var percent = part.EndsWith('%');
            if (!double.TryParse(percent ? part[..^1] : part, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)) return false;
            var fraction = percent ? number / 100 : number;
            if (fraction is < 0 or > 1) return false;
            alpha = (byte)Math.Round(fraction * 255, MidpointRounding.AwayFromZero);
            hasAlpha = true;
        }

        color = new Rgba(alpha, (byte)channels[0], (byte)channels[1], (byte)channels[2]);
        return true;
    }
}
