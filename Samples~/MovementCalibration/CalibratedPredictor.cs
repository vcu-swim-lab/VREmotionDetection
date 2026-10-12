using System;
using UnityEngine;

// Movement calibration: the player makes each of the seven faces once (about 10 seconds
// each), then every prediction is matched against this player's own faces. Without
// calibration, `Predict` works exactly as before.
[RequireComponent(typeof(NewFaceAuModel))]
public class CalibratedPredictor : MonoBehaviour
{
    private NewFaceAuModel predictor;

    void Start()
    {
        predictor = GetComponent<NewFaceAuModel>();
        _ = Run();
    }

    private async Awaitable Run()
    {
        // 1. Calibrate. Skip this block to use the model without calibration.
        predictor.StartCalibration();

        foreach (Emotion emotion in Enum.GetValues(typeof(Emotion)))
        {
            // Show your own prompt here, e.g. "Make a Happiness face and hold it".
            Debug.Log($"Make a {emotion} face and hold it.");

            if (!await predictor.CalibrateEmotion(emotion))
            {
                Debug.LogWarning($"Face tracking was lost while collecting {emotion}.");
            }
        }

        if (!predictor.FinishCalibration())
        {
            // The model still works; it is just not calibrated.
            Debug.LogWarning($"Calibration failed: {predictor.CalibrationError}");
        }

        // 2. Predict. Calibrated when calibration succeeded, the model's own answer otherwise.
        while (true)
        {
            var (emotion, confidence) = await predictor.Predict();
            Debug.Log($"{emotion} (calibrated: {predictor.IsCalibrated})");
        }
    }
}
