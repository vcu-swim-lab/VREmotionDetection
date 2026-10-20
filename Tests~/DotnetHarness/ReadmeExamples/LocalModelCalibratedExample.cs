using UnityEngine;
using VcuSeCenter.VREmotionDetection;

[RequireComponent(typeof(NewFaceAuModel))]
public class LocalModelCalibratedExample : MonoBehaviour
{
    [SerializeField] private string playerId = "player1";

    private NewFaceAuModel predictor;

    void Start()
    {
        predictor = GetComponent<NewFaceAuModel>();

        // 1. Choose the backend first: a calibration belongs to the backend that made it.
        predictor.UseLocalModel();

        // 2. Switch calibration on before you load or start one.
        predictor.UseCalibration = true;

        _ = Run();
    }

    private async Awaitable Run()
    {
        // 3. Reuse this player's saved calibration if there is one...
        if (!predictor.LoadCalibration(playerId))
        {
            // 4. ...otherwise calibrate: seven emotions, about ten seconds each.
            bool calibrated = await predictor.CalibrateAll(ShowPrompt);

            if (calibrated)
            {
                // 5. Keep it for next time.
                predictor.SaveCalibration(playerId);
            }
            else
            {
                Debug.LogWarning($"Calibration failed: {predictor.CalibrationError}");
            }
        }

        // 6. Predict. If calibration failed, this is simply the uncalibrated answer.
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
