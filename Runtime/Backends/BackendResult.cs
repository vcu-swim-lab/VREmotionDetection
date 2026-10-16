using System;

namespace VcuSwimLab.Backends
{
    public readonly struct BackendResult
    {
        private BackendResult(float[] scores, float[] raw, PredictionError? error, bool referenceFacesSent)
        {
            Scores = scores;
            Raw = raw;
            Error = error;
            ReferenceFacesSent = referenceFacesSent;
        }

        public float[] Scores { get; }

        public float[] Raw { get; }

        public PredictionError? Error { get; }

        public bool ReferenceFacesSent { get; }

        public bool Ok => Scores != null;

        public static BackendResult Success(float[] scores, float[] raw = null, bool referenceFacesSent = false)
        {
            if (scores == null)
            {
                throw new ArgumentNullException(nameof(scores));
            }

            return new BackendResult(scores, raw, null, referenceFacesSent);
        }

        public static BackendResult Failure(PredictionError error)
        {
            return new BackendResult(null, null, error, false);
        }

        public static BackendResult Failure(PredictionErrorCode code, string message, string backendId = null)
        {
            return Failure(new PredictionError(code, message, backendId));
        }
    }
}
