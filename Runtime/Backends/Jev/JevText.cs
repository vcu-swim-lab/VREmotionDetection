using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace VcuSeCenter.Backends.Jev
{
    internal static class JevText
    {
        internal const int MaxLength = 200;
        internal const int MaxInspected = 65536;

        private const string Redacted = "<redacted>";

        private static readonly Regex Credential = new Regex(
            @"(authorization|x-api-key|api[-_]?key|x-auth-token)[""']?\s*[:=]\s*[""']?[^\s""',;}]+(?:\s+[^\s""',;}]+)?",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private static readonly Regex BearerValue = new Regex(
            @"Bearer\s+[A-Za-z0-9._~+/=-]{6,}", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private static readonly Regex UnicodeEscape = new Regex(@"\\u([0-9a-fA-F]{4})", RegexOptions.CultureInvariant);

        internal static string Sanitize(string text, string apiKey)
        {
            if (string.IsNullOrEmpty(text))
            {
                return text;
            }

            string cleaned = text.Length > MaxInspected ? text.Substring(0, MaxInspected) : text;
            cleaned = cleaned.Replace("\\/", "/");
            cleaned = UnicodeEscape.Replace(cleaned, match => ((char)int.Parse(match.Groups[1].Value, NumberStyles.HexNumber)).ToString());
            if (!string.IsNullOrEmpty(apiKey))
            {
                cleaned = cleaned.Replace(apiKey, Redacted);
            }

            cleaned = Credential.Replace(cleaned, match => match.Groups[1].Value + ": " + Redacted);
            cleaned = BearerValue.Replace(cleaned, "Bearer " + Redacted);
            return Cap(Flatten(cleaned));
        }

        private static string Flatten(string text)
        {
            var builder = new StringBuilder(text.Length);
            bool space = false;
            foreach (char c in text)
            {
                if (char.GetUnicodeCategory(c) == UnicodeCategory.Format)
                {
                    continue;
                }

                if (char.IsControl(c) || char.IsWhiteSpace(c))
                {
                    space = builder.Length > 0;
                    continue;
                }

                if (space)
                {
                    builder.Append(' ');
                    space = false;
                }

                builder.Append(c);
            }

            return builder.ToString();
        }

        private static string Cap(string text)
        {
            if (text.Length <= MaxLength)
            {
                return text;
            }

            int length = char.IsHighSurrogate(text[MaxLength - 1]) ? MaxLength - 1 : MaxLength;
            return text.Substring(0, length);
        }
    }
}
