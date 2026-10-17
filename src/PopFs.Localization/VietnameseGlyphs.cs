using System.Text;

namespace PopFs.Localization;

internal static class VietnameseGlyphs
{
    public static IReadOnlyList<int> UppercaseSet()
    {
        var result = new SortedSet<int>();
        const string bases = "AĂÂEÊIOÔƠUƯY";
        const string tones = "\u0300\u0301\u0303\u0309\u0323";
        foreach (var letter in bases)
        {
            result.Add(letter);
            foreach (var tone in tones)
            {
                var normalized = string.Concat(letter, tone).Normalize(NormalizationForm.FormC);
                if (normalized.Length == 1) result.Add(normalized[0]);
            }
        }
        result.Add('Đ');
        return result.ToArray();
    }

    public static IReadOnlyList<int> BasicSet()
    {
        var result = new SortedSet<int>(Enumerable.Range(32, 95));
        const string bases = "aăâeêioôơuưyAĂÂEÊIOÔƠUƯY";
        const string tones = "\u0300\u0301\u0303\u0309\u0323";
        foreach (var letter in bases)
        {
            result.Add(letter);
            foreach (var tone in tones)
            {
                var normalized = string.Concat(letter, tone).Normalize(NormalizationForm.FormC);
                if (normalized.Length == 1) result.Add(normalized[0]);
            }
        }
        result.Add('đ');
        result.Add('Đ');
        result.UnionWith(new[] { 0x00A0, 0x00AE, 0x2019, 0x2026, 0x2122 });
        return result.ToArray();
    }
}
