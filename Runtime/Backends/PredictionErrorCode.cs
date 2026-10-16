namespace VcuSwimLab.Backends
{
    public enum PredictionErrorCode
    {
        None = 0,
        NotConfigured,
        NotReady,
        Busy,
        NoTracking,
        InvalidInput,
        Unauthorized,
        InvalidRequest,
        RateLimited,
        Timeout,
        Unreachable,
        ServerError,
        InvalidResponse,
        Internal,
    }
}
