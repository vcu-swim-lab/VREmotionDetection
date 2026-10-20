using VcuSeCenter.Backends;

namespace VcuSeCenter.VREmotionDetection
{
    public readonly struct PredictionOutcome
    {
        private readonly bool succeeded;

        private PredictionOutcome(bool succeeded, FacePrediction prediction, PredictionError? error)
        {
            this.succeeded = succeeded;
            Prediction = prediction;
            Error = error;
        }

        public bool Ok => succeeded;

        public FacePrediction Prediction { get; }

        public PredictionError? Error { get; }

        public FacePrediction? PredictionOrNull => succeeded ? Prediction : (FacePrediction?)null;

        internal static PredictionOutcome Success(FacePrediction prediction)
        {
            return new PredictionOutcome(true, prediction, null);
        }

        internal static PredictionOutcome Failure(PredictionError error)
        {
            return new PredictionOutcome(false, default, error);
        }
    }
}
