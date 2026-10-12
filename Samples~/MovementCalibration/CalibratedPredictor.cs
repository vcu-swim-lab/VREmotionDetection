using System;
using UnityEngine;

// Movement calibration: the player makes each of the seven faces once (about 10 seconds
// each), then every prediction is matched against this player's own faces. The calibration
// is saved on this device for the player, so next time it can be loaded instead of redone.
// Tick "Use Calibration" on the New Face Au Model component to turn it on; unticked (the
// default), this script just predicts with the model's own answer.
[RequireComponent(typeof(NewFaceAuModel))]
public class CalibratedPredictor : MonoBehaviour
{
    // Any id your game uses for the player: letters, digits, '-' and '_'.
    [SerializeField] private string playerId = "player1";

    private NewFaceAuModel predictor;

    void Start()
    {
        predictor = GetComponent<NewFaceAuModel>();
        _ = Run();
    }

    private async Awaitable Run()
    {
        if (predictor.UseCalibration && !predictor.LoadCalibration(playerId))
        {
            await Calibrate();
        }

        // Calibrated when calibration succeeded, the model's own answer otherwise.
        while (true)
        {
            var (emotion, confidence) = await predictor.Predict();
            Debug.Log($"{emotion} ({confidence:F2}, calibrated: {predictor.IsCalibrated})");
        }
    }

    private async Awaitable Calibrate()
    {
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

        if (predictor.FinishCalibration())
        {
            predictor.SaveCalibration(playerId);
        }
        else
        {
            // The model still works; it is just not calibrated.
            Debug.LogWarning($"Calibration failed: {predictor.CalibrationError}");
        }
    }
}
