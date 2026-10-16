using System;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.Events;
using VcuSwimLab.Backends;
using VcuSwimLab.Backends.Jev;

[RequireComponent(typeof(NewFaceAuModel))]
public class CalibrationCheck : MonoBehaviour
{
    [SerializeField] private int predictionsPerEmotion = 5;

    public UnityEvent<string> onMessage;

    private NewFaceAuModel predictor;
    private IEmotionBackend comparison;

    void Start()
    {
        predictor = GetComponent<NewFaceAuModel>();
        comparison = CreateComparison();
        _ = Run();
    }

    private IEmotionBackend CreateComparison()
    {
        try
        {
            return predictor.CreateJevBackend(JevOptions.FromEnvironment());
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private void Say(string message)
    {
        Debug.Log($"[CalibrationCheck] {message}");
        onMessage?.Invoke(message);
    }

    private async Awaitable Run()
    {
        var emotions = (Emotion[])Enum.GetValues(typeof(Emotion));

        predictor.UseCalibration = true;

        bool calibrated = await predictor.CalibrateAll(async emotion =>
        {
            Say($"Calibration: make a {emotion} face and hold it.");
            await Awaitable.WaitForSecondsAsync(2f);
        });

        if (!calibrated)
        {
            Say($"Calibration failed: {predictor.CalibrationError}");
            return;
        }

        bool compare = comparison != null && predictor.ActiveBackendId != comparison.Id;
        int total = 0, valid = 0, rawCorrect = 0, calibratedCorrect = 0, comparisonCorrect = 0, changed = 0;
        var report = new StringBuilder("Emotion      uncalibrated  calibrated" + (compare ? "  " + comparison.Id : "") + "\n");

        foreach (var emotion in emotions)
        {
            Say($"Test: make a {emotion} face and hold it.");
            await Awaitable.WaitForSecondsAsync(3f);

            int raw = 0, withCalibration = 0, other = 0;
            for (int i = 0; i < predictionsPerEmotion; ++i)
            {
                float next = Time.time + 1f;
                var prediction = await predictor.PredictDetailed();
                total++;

                if (prediction.HasValue)
                {
                    valid++;
                    var model = prediction.Value.Emotion;
                    var calibratedEmotion = predictor.CalibratedEmotion(prediction.Value) ?? model;
                    if (model == emotion) raw++;
                    if (calibratedEmotion == emotion) withCalibration++;
                    if (calibratedEmotion != model) changed++;

                    if (compare)
                    {
                        var result = await predictor.ScoreWith(comparison, prediction.Value.Window);
                        if (result.Ok && ScoreValidation.Argmax(result.Scores) == (int)emotion) other++;
                    }
                }

                float wait = next - Time.time;
                if (wait > 0) await Awaitable.WaitForSecondsAsync(wait);
            }

            rawCorrect += raw;
            calibratedCorrect += withCalibration;
            comparisonCorrect += other;
            report.AppendLine($"{emotion,-12} {raw}/{predictionsPerEmotion,-12} {withCalibration}/{predictionsPerEmotion}" +
                              (compare ? $"  {other}/{predictionsPerEmotion}" : ""));
        }

        report.AppendLine($"All          {rawCorrect}/{total,-12} {calibratedCorrect}/{total}" +
                          (compare ? $"  {comparisonCorrect}/{total}" : ""));
        report.AppendLine($"Valid predictions: {valid}/{total}. Calibration changed {changed} answers.");
        Say(report.ToString());
    }
}
