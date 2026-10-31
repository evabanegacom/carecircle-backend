using System.Text.RegularExpressions;

namespace PeerCare.Services;

public sealed record SafetyScan(IReadOnlyList<string> Flags, bool HasDose, bool Emergency);

public static partial class SafetyScanner
{
    [GeneratedRegex(@"\b\d+(?:\.\d+)?\s?(?:mg|ml|mcg|µg|g|tablets?|capsules?|puffs?|drops?)\b|\b\d+\s?(?:times|x)\s?(?:a\s)?day\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DosePattern();
    [GeneratedRegex(@"(?:\+?\d[\d\s().-]{7,}\d)", RegexOptions.CultureInvariant)]
    private static partial Regex PhonePattern();
    [GeneratedRegex(@"\b(?:https?://|www\.)\S+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex LinkPattern();
    [GeneratedRegex(@"\b(?:cure(?:d|s)?|guaranteed cure|works for everyone|miracle treatment)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex CurePattern();
    [GeneratedRegex(@"\b(?:buy now|dm to order|whatsapp me|for sale|limited offer|promo code|contact me for|selling)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SellingPattern();

    public static SafetyScan Scan(params string?[] values)
    {
        var text = string.Join(" ", values.Where(v => !string.IsNullOrWhiteSpace(v)));
        var flags = new List<string>();
        var dose = DosePattern().IsMatch(text);
        if (dose) flags.Add("Possible dosage details. Remove dose amounts before sharing.");
        if (PhonePattern().IsMatch(text)) flags.Add("Possible phone number. Contact details are not allowed in public stories.");
        if (LinkPattern().IsMatch(text)) flags.Add("Contains a link and needs moderator review.");
        if (CurePattern().IsMatch(text)) flags.Add("Possible cure claim. A moderator will review this wording.");
        if (SellingPattern().IsMatch(text)) flags.Add("Possible selling language. A moderator will review this post.");
        var emergency = Regex.IsMatch(text,
            @"\b(can't breathe|cannot breathe|difficulty breathing|trouble breathing|unconscious|not waking|seizure now|severe bleeding|blue lips|choking)\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        return new SafetyScan(flags, dose, emergency);
    }
}
