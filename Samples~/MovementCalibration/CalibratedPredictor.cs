using UnityEngine;
using VcuSwimLab.VREmotionDetection;

[RequireComponent(typeof(NewFaceAuModel))]
public class CalibratedPredictor : MonoBehaviour
{
    [SerializeField] private string playerId = "player1";

    private NewFaceAuModel predictor;

    void Start()
    {
        predictor = GetComponent<NewFaceAuModel>();

        predictor.UseSparseVr();

        predictor.UseCalibration = true;

        _ = Run();
    }

    private async Awaitable Run()
    {
        if (!predictor.LoadCalibration(playerId))
        {
            bool calibrated = await predictor.CalibrateAll(ShowPrompt);

            if (calibrated)
            {
                predictor.SaveCalibration(playerId);
            }
            else
            {
                Debug.LogWarning($"Calibration failed: {predictor.CalibrationError}");
            }
        }

        while (!destroyCancellationToken.IsCancellationRequested)
        {
            var (emotion, confidence) = await predictor.Predict();
            Debug.Log($"{emotion} ({confidence:F2}), calibrated: {predictor.IsCalibrated}");
        }
    }

    private static async Awaitable ShowPrompt(Emotion emotion)
    {
        Debug.Log($"Make a {emotion} face and hold it.");
        await Awaitable.WaitForSecondsAsync(2f);
    }
}
