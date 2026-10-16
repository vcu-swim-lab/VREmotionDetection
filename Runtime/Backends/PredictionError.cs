namespace VcuSwimLab.Backends
{
    public readonly struct PredictionError
    {
        public PredictionError(PredictionErrorCode code, string message, string backendId = null)
        {
            Code = code;
            Message = message ?? string.Empty;
            BackendId = backendId;
        }

        public PredictionErrorCode Code { get; }

        public string Message { get; }

        public string BackendId { get; }

        public bool IsTransient
        {
            get
            {
                switch (Code)
                {
                    case PredictionErrorCode.RateLimited:
                    case PredictionErrorCode.Timeout:
                    case PredictionErrorCode.Unreachable:
                    case PredictionErrorCode.ServerError:
                        return true;
                    default:
                        return false;
                }
            }
        }

        public override string ToString()
        {
            return string.IsNullOrEmpty(BackendId) ? $"{Code}: {Message}" : $"{BackendId} {Code}: {Message}";
        }
    }
}
