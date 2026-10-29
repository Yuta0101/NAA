using System.Text.RegularExpressions;

namespace API_NAA.Services;

/// <summary>
/// Normalizes card identifiers so binding and login compare the same text.
/// Keyboard-emulation readers may add spaces, colons or dashes and may send
/// hexadecimal digits in lower case.
/// </summary>
internal static partial class CardUid
{
    public const int MaxLength = 32;

    [GeneratedRegex("^[0-9A-F]{4,32}$")]
    private static partial Regex ValidUid();

    public static string? Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var cleaned = new string(value
            .Where(character => !char.IsWhiteSpace(character) && character is not (':' or '-'))
            .ToArray())
            .ToUpperInvariant();

        return ValidUid().IsMatch(cleaned) ? cleaned : null;
    }

    public static string Mask(string uid)
    {
        if (uid.Length <= 4)
            return uid;

        return new string('•', Math.Min(uid.Length - 4, 6)) + uid[^4..];
    }
}
