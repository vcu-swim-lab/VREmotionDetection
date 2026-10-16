using UnityEngine;
using VcuSwimLab.Backends.Jev;

[RequireComponent(typeof(NewFaceAuModel))]
public class JevCalibratedExample : MonoBehaviour
{
    [SerializeField] private string playerId = "player1";

    private NewFaceAuModel predictor;

    void Start()
    {
        predictor = GetComponent<NewFaceAuModel>();
        predictor.PredictionFailed += error => Debug.LogWarning(error);

        // 1. Choose the backend first, with your own key. Choosing a different backend later
        //    clears any calibration, because a calibration belongs to the backend that made it.
        var options = JevOptions.FromEnvironment();
        // options.SendOwnFacesInRequest = true;   // optional: also tell Jev how this player makes each face
        predictor.UseJev(options);

        // 2. Switch calibration on before you load or start one.
        predictor.UseCalibration = true;

        _ = Run();
    }

    private async Awaitable Run()
    {
        // 3. Reuse this player's saved Jev calibration if there is one...
        if (!predictor.LoadCalibration(playerId))
        {
            // 4. ...otherwise calibrate. Every sample is one request to Jev:
            //    about 70 requests, around a minute and a half.
            bool calibrated = await predictor.CalibrateAll(ShowPrompt);

            if (calibrated)
            {
                // 5. Keep it for next time.
                predictor.SaveCalibration(playerId);
            }
            else
            {
                Debug.LogWarning($"Calibration failed: {predictor.CalibrationError} {predictor.LastError}");
            }
        }

        // 6. Predict, waiting a little after a failure.
        while (!destroyCancellationToken.IsCancellationRequested)
        {
            var (emotion, confidence) = await predictor.Predict();

            if (predictor.LastError.HasValue)
            {
                await Awaitable.WaitForSecondsAsync(2f);
                continue;
            }

            Debug.Log($"{emotion} ({confidence:F2}), calibrated: {predictor.IsCalibrated}");
        }
    }

    private static async Awaitable ShowPrompt(Emotion emotion)
    {
        Debug.Log($"Make a {emotion} face and hold it.");
        await Awaitable.WaitForSecondsAsync(2f);
    }
}
