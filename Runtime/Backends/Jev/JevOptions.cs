using System;
using System.Collections.Generic;
using System.Linq;

namespace VcuSwimLab.Backends.Jev
{
    public sealed class JevOptions
    {
        public const string ApiKeyVariable = "TYPESAFE_API_KEY";
        public const string DefaultBaseUrl = "https://api.typesafe.ai";
        public const string DefaultModel = "jev-1.13.0";
        public const float DefaultTimeoutSeconds = 10f;
        public const float MaxTimeoutSeconds = 120f;
        public const int DefaultMaxRetries = 2;
        public const int MaxAllowedRetries = 5;

        public JevOptions(string apiKey)
        {
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                throw new ArgumentException("an API key is required", nameof(apiKey));
            }

            string trimmed = apiKey.Trim();
            if (trimmed.Any(c => c > 127 || char.IsWhiteSpace(c) || char.IsControl(c)))
            {
                throw new ArgumentException("the API key contains characters that cannot be part of a key", nameof(apiKey));
            }

            ApiKey = trimmed;
        }

        public string ApiKey { get; }

        public string BaseUrl { get; set; } = DefaultBaseUrl;

        public string Model { get; set; } = DefaultModel;

        public float TimeoutSeconds { get; set; } = DefaultTimeoutSeconds;

        public int MaxRetries { get; set; } = DefaultMaxRetries;

        public bool SendOwnFacesInRequest { get; set; }

        public bool LogRequests { get; set; }

        public bool IncludeServerDetails { get; set; }

        public static JevOptions FromEnvironment()
        {
            string key = Environment.GetEnvironmentVariable(ApiKeyVariable);
            if (string.IsNullOrWhiteSpace(key))
            {
                throw new InvalidOperationException($"the {ApiKeyVariable} environment variable is not set");
            }

            return new JevOptions(key);
        }

        public void Validate()
        {
            var problems = new List<string>();

            if (!Uri.TryCreate(BaseUrl?.Trim(), UriKind.Absolute, out Uri uri))
            {
                problems.Add("BaseUrl must be an absolute URL");
            }
            else
            {
                if (uri.Scheme != Uri.UriSchemeHttps && !(uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback))
                {
                    problems.Add("BaseUrl must use https (http is accepted only for localhost)");
                }

                if (uri.UserInfo.Length > 0 || uri.Query.Length > 0 || uri.Fragment.Length > 0)
                {
                    problems.Add("BaseUrl must not contain credentials, a query or a fragment");
                }
            }

            if (string.IsNullOrWhiteSpace(Model))
            {
                problems.Add("Model must not be empty");
            }

            if (!(TimeoutSeconds > 0f && TimeoutSeconds <= MaxTimeoutSeconds))
            {
                problems.Add($"TimeoutSeconds must be above 0 and at most {MaxTimeoutSeconds}");
            }

            if (MaxRetries < 0 || MaxRetries > MaxAllowedRetries)
            {
                problems.Add($"MaxRetries must be between 0 and {MaxAllowedRetries}");
            }

            if (problems.Count > 0)
            {
                throw new ArgumentException("invalid Jev options: " + string.Join("; ", problems));
            }
        }

        public override string ToString()
        {
            return $"JevOptions(BaseUrl={BaseUrl}, Model={Model}, ApiKey=<redacted>)";
        }
    }
}
