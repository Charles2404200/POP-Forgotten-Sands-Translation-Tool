using System.Text;
using System.Text.RegularExpressions;

namespace PopFs.Localization;

internal static partial class DisplayTextCase
{
    // Format arguments and input-control tags are case-sensitive game syntax.
    [GeneratedRegex(@"^%(?:[-+#0 ]*\d*(?:\.\d+)?(?:hh|ll|[hljztL])?[diuoxXfFeEgGaAcspn]|%)")]
    private static partial Regex PrintfToken();

    public static string Uppercase(string value)
    {
        var output = new StringBuilder(value.Length);
        var position = 0;
        while (position < value.Length)
        {
            var next = value.IndexOfAny(['{', '%'], position);
            if (next < 0) next = value.Length;
            output.Append(value[position..next].ToUpperInvariant());
            if (next == value.Length) break;

            if (value[next] == '{')
            {
                var end = value.IndexOf('}', next + 1);
                if (end >= 0)
                {
                    output.Append(value.AsSpan(next, end - next + 1));
                    position = end + 1;
                    continue;
                }
            }
            else
            {
                var token = PrintfToken().Match(value[next..]);
                if (token.Success)
                {
                    output.Append(token.Value);
                    position = next + token.Length;
                    continue;
                }
            }
            output.Append(value[next]);
            position = next + 1;
        }
        return output.ToString().Normalize(NormalizationForm.FormC);
    }
}
