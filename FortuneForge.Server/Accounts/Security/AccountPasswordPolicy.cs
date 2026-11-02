using System.Globalization;
using System.Text;

namespace FortuneForge.Server.Accounts;

internal static class AccountPasswordPolicy
{
    internal const int MinimumLength = 8;
    internal const int MaximumLength = 128;

    internal static bool IsValidNewPassword(string? value)
    {
        if (value is null || value.Length is < MinimumLength or > MaximumLength)
        {
            return false;
        }

        var hasUppercase = false;
        var hasDigit = false;
        var hasSymbol = false;

        foreach (var rune in value.EnumerateRunes())
        {
            hasUppercase |= rune.Value is >= 'A' and <= 'Z';
            hasDigit |= rune.Value is >= '0' and <= '9';

            var category = Rune.GetUnicodeCategory(rune);
            hasSymbol |= category is
                UnicodeCategory.ConnectorPunctuation or
                UnicodeCategory.DashPunctuation or
                UnicodeCategory.OpenPunctuation or
                UnicodeCategory.ClosePunctuation or
                UnicodeCategory.InitialQuotePunctuation or
                UnicodeCategory.FinalQuotePunctuation or
                UnicodeCategory.OtherPunctuation or
                UnicodeCategory.MathSymbol or
                UnicodeCategory.CurrencySymbol or
                UnicodeCategory.ModifierSymbol or
                UnicodeCategory.OtherSymbol;
        }

        return hasUppercase && hasDigit && hasSymbol;
    }
}
