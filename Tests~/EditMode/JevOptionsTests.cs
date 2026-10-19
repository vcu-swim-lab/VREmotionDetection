using System;
using NUnit.Framework;
using VcuSwimLab.Backends.Jev;

public class JevOptionsTests
{
    [Test]
    public void An_api_key_is_required()
    {
        Assert.Throws<ArgumentException>(() => new JevOptions(null));
        Assert.Throws<ArgumentException>(() => new JevOptions(""));
        Assert.Throws<ArgumentException>(() => new JevOptions("   "));
    }

    [Test]
    public void A_key_with_spaces_or_non_ascii_characters_is_refused_without_echoing_it()
    {
        var spaced = Assert.Throws<ArgumentException>(() => new JevOptions("abc def"));
        var accented = Assert.Throws<ArgumentException>(() => new JevOptions("clé-secrète"));

        StringAssert.DoesNotContain("abc def", spaced.Message);
        StringAssert.DoesNotContain("secrète", accented.Message);
    }

    [Test]
    public void The_key_is_trimmed_and_never_printed()
    {
        var options = new JevOptions("  key-123  ");

        Assert.AreEqual("key-123", options.ApiKey);
        StringAssert.DoesNotContain("key-123", options.ToString());
    }

    [Test]
    public void Defaults_are_valid()
    {
        var options = new JevOptions("key");

        Assert.DoesNotThrow(options.Validate);
        Assert.AreEqual(JevOptions.DefaultBaseUrl, options.BaseUrl);
        Assert.AreEqual(JevOptions.DefaultModel, options.Model);
        Assert.AreEqual(JevOptions.DefaultTimeoutSeconds, options.TimeoutSeconds);
        Assert.AreEqual(JevOptions.DefaultMaxRetries, options.MaxRetries);
        Assert.IsFalse(options.SendOwnFacesInRequest);
    }

    [TestCase("not a url")]
    [TestCase("http://api.example.com")]
    [TestCase("ftp://api.example.com")]
    [TestCase("")]
    [TestCase(null)]
    public void A_base_url_must_be_https(string url)
    {
        var options = new JevOptions("key") { BaseUrl = url };

        Assert.Throws<ArgumentException>(options.Validate);
    }

    [TestCase("http://localhost:8080")]
    [TestCase("http://127.0.0.1:8080")]
    [TestCase("https://proxy.example.com/typesafe")]
    public void Https_and_local_proxies_are_accepted(string url)
    {
        var options = new JevOptions("key") { BaseUrl = url };

        Assert.DoesNotThrow(options.Validate);
    }

    [TestCase("https://user:secret@api.example.com")]
    [TestCase("https://api.example.com?x=1")]
    [TestCase("https://api.example.com/?x=1")]
    [TestCase("https://api.example.com/#section")]
    public void A_base_url_with_credentials_a_query_or_a_fragment_is_refused_without_echoing_it(string url)
    {
        var options = new JevOptions("key") { BaseUrl = url };

        var thrown = Assert.Throws<ArgumentException>(options.Validate);

        StringAssert.Contains("credentials, a query or a fragment", thrown.Message);
        StringAssert.DoesNotContain("secret", thrown.Message);
    }

    [Test]
    public void Spaces_around_the_base_url_are_ignored()
    {
        var options = new JevOptions("key") { BaseUrl = "  https://proxy.example.com/typesafe/  " };

        Assert.DoesNotThrow(options.Validate);
    }

    [TestCase(float.NaN)]
    [TestCase(0f)]
    [TestCase(-1f)]
    [TestCase(120.5f)]
    [TestCase(float.PositiveInfinity)]
    public void The_timeout_must_be_above_zero_and_at_most_the_maximum(float seconds)
    {
        var options = new JevOptions("key") { TimeoutSeconds = seconds };

        StringAssert.Contains("TimeoutSeconds", Assert.Throws<ArgumentException>(options.Validate).Message);
    }

    [TestCase(0.001f)]
    [TestCase(10f)]
    [TestCase(120f)]
    public void Timeouts_inside_the_range_are_accepted(float seconds)
    {
        Assert.DoesNotThrow(new JevOptions("key") { TimeoutSeconds = seconds }.Validate);
    }

    [TestCase(-1)]
    [TestCase(6)]
    public void Retries_outside_zero_to_five_are_refused(int retries)
    {
        StringAssert.Contains("MaxRetries", Assert.Throws<ArgumentException>(new JevOptions("key") { MaxRetries = retries }.Validate).Message);
    }

    [TestCase(0)]
    [TestCase(5)]
    public void Zero_to_five_retries_are_accepted(int retries)
    {
        Assert.DoesNotThrow(new JevOptions("key") { MaxRetries = retries }.Validate);
    }

    [Test]
    public void Request_logging_and_the_servers_text_are_off_by_default()
    {
        var options = new JevOptions("key");

        Assert.IsFalse(options.LogRequests);
        Assert.IsFalse(options.IncludeServerDetails);
        Assert.IsFalse(options.SendOwnFacesInRequest);
    }

    [Test]
    public void Every_problem_is_listed_together()
    {
        var options = new JevOptions("key") { BaseUrl = "nope", Model = " ", TimeoutSeconds = 0f, MaxRetries = 9 };

        var thrown = Assert.Throws<ArgumentException>(options.Validate);

        StringAssert.Contains("BaseUrl", thrown.Message);
        StringAssert.Contains("Model", thrown.Message);
        StringAssert.Contains("TimeoutSeconds", thrown.Message);
        StringAssert.Contains("MaxRetries", thrown.Message);
    }

    [Test]
    public void The_environment_variable_is_read_only_when_asked_for()
    {
        string previous = Environment.GetEnvironmentVariable(JevOptions.ApiKeyVariable);
        try
        {
            Environment.SetEnvironmentVariable(JevOptions.ApiKeyVariable, null);
            Assert.Throws<InvalidOperationException>(() => JevOptions.FromEnvironment());

            Environment.SetEnvironmentVariable(JevOptions.ApiKeyVariable, "from-environment");
            Assert.AreEqual("from-environment", JevOptions.FromEnvironment().ApiKey);
        }
        finally
        {
            Environment.SetEnvironmentVariable(JevOptions.ApiKeyVariable, previous);
        }
    }
}
