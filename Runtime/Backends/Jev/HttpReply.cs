namespace VcuSwimLab.Backends.Jev
{
    public readonly struct HttpReply
    {
        public HttpReply(long statusCode, string body, float? retryAfterSeconds = null)
        {
            StatusCode = statusCode;
            Body = body;
            RetryAfterSeconds = retryAfterSeconds;
        }

        public long StatusCode { get; }

        public string Body { get; }

        public float? RetryAfterSeconds { get; }
    }
}
