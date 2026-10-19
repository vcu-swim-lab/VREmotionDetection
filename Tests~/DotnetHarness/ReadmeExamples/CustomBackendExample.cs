using UnityEngine;
using VcuSwimLab.Backends;
using VcuSwimLab.VREmotionDetection;

public sealed class AlwaysNeutralBackend : IEmotionBackend
{
    public string Id => "always-neutral";

    public async Awaitable<BackendResult> ScoreAsync(BackendRequest request)
    {
        if (!WindowValidation.TryValidate(request.Window, 1, 63, out string problem))
        {
            return BackendResult.Failure(PredictionErrorCode.InvalidInput, problem, Id);
        }

        var scores = new float[request.ClassNames.Count];
        scores[(int)Emotion.Neutral] = 1f;
        return BackendResult.Success(scores);
    }

    public float[] PrepareForCalibration(float[] scores) => scores;

    public void Dispose()
    {
    }
}
