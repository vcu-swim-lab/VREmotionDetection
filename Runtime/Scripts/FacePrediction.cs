
namespace VcuSwimLab.VREmotionDetection
{
    public readonly struct FacePrediction
    {
        public readonly Emotion Emotion;
        public readonly float Confidence;
        public readonly float[] Scores;
        public readonly float[][] Window;
        public readonly float[] Raw;
        public readonly long Generation;
        public readonly string BackendId;

        internal FacePrediction(Emotion emotion, float confidence, float[] scores, float[][] window, long generation,
                                float[] raw = null, string backendId = null)
        {
            Emotion = emotion;
            Confidence = confidence;
            Scores = scores;
            Window = window;
            Generation = generation;
            Raw = raw;
            BackendId = backendId;
        }

        public float ScoreOf(Emotion emotion) => (Raw ?? Scores)[(int)emotion];
    }
}
