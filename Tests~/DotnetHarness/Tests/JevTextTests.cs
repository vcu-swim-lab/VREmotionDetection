using NUnit.Framework;
using VcuSeCenter.Backends.Jev;

// JevText.Sanitize cleans every piece of the service's text before it reaches an error message or a log line
// (added after the baseline; see the 3.0.0 "Fixed" entries about key leaks and unbounded server text).
public class JevTextTests
{
    private const string Key = "sk-live-0123456789abcdef";

    [Test]
    public void Null_and_empty_text_come_back_unchanged()
    {
        Assert.IsNull(JevText.Sanitize(null, Key));
        Assert.AreEqual(string.Empty, JevText.Sanitize(string.Empty, Key));
    }

    [Test]
    public void The_key_is_replaced_wherever_it_appears()
    {
        string cleaned = JevText.Sanitize($"first {Key} then {Key}", Key);

        StringAssert.DoesNotContain(Key, cleaned);
        StringAssert.Contains("<redacted>", cleaned);
    }

    [TestCase("Authorization: Bearer abcdef0123456789")]
    [TestCase("authorization=Bearer abcdef0123456789")]
    [TestCase("\"Authorization\": \"Bearer abcdef0123456789\"")]
    [TestCase("x-api-key: abcdef0123456789")]
    [TestCase("X-Api-Key=abcdef0123456789")]
    [TestCase("api_key=abcdef0123456789")]
    [TestCase("apikey: abcdef0123456789")]
    [TestCase("x-auth-token: abcdef0123456789")]
    [TestCase("rejected Bearer abcdef0123456789 for this call")]
    public void Credentials_in_the_servers_text_are_removed_even_when_they_are_not_our_key(string text)
    {
        string cleaned = JevText.Sanitize(text, Key);

        StringAssert.DoesNotContain("abcdef0123456789", cleaned);
        StringAssert.Contains("<redacted>", cleaned);
    }

    [Test]
    public void Escaped_text_is_unescaped_before_the_key_is_looked_for()
    {
        // 0 is the digit 0, so this spells the key once the escape is resolved.
        string cleaned = JevText.Sanitize("sk-live-\\u0030123456789abcdef", Key);

        StringAssert.DoesNotContain(Key, cleaned);
        StringAssert.DoesNotContain("u0030", cleaned);
        StringAssert.Contains("<redacted>", cleaned);

        Assert.AreEqual("https://api.example.com/v1", JevText.Sanitize("https:\\/\\/api.example.com\\/v1", Key));
    }

    [Test]
    public void Control_and_zero_width_characters_are_removed_and_whitespace_is_flattened()
    {
        Assert.AreEqual("ab c d", JevText.Sanitize("a​b\r\n\tc\u0007 d", Key));
        Assert.AreEqual("x y", JevText.Sanitize("  x \n\n y  ", Key));
        Assert.AreEqual(string.Empty, JevText.Sanitize(" \r\n\t ", Key));
    }

    [Test]
    public void Text_is_cut_to_200_characters_without_splitting_a_surrogate_pair()
    {
        Assert.AreEqual(JevText.MaxLength, JevText.Sanitize(new string('x', 500), Key).Length);
        Assert.AreEqual(JevText.MaxLength, JevText.Sanitize(new string('x', JevText.MaxLength), Key).Length);
        Assert.AreEqual(JevText.MaxLength - 1, JevText.Sanitize(new string('x', JevText.MaxLength - 1), Key).Length);

        string emoji = new string('x', JevText.MaxLength - 1) + "\U0001F600" + "tail";
        string cut = JevText.Sanitize(emoji, Key);

        Assert.AreEqual(JevText.MaxLength - 1, cut.Length, "the pair would straddle the limit, so it is dropped whole");
        Assert.IsFalse(char.IsHighSurrogate(cut[cut.Length - 1]));
    }

    [Test]
    public void Only_the_first_65536_characters_are_inspected()
    {
        string huge = new string('a', JevText.MaxInspected + 5000) + Key;

        Assert.AreEqual(JevText.MaxLength, JevText.Sanitize(huge, Key).Length);
    }

    [Test]
    public void The_key_is_removed_before_the_text_is_cut_so_half_a_key_cannot_survive()
    {
        string cleaned = JevText.Sanitize(new string('x', JevText.MaxLength - 5) + Key, Key);

        StringAssert.DoesNotContain(Key.Substring(0, 6), cleaned);
        Assert.LessOrEqual(cleaned.Length, JevText.MaxLength);
    }
}
