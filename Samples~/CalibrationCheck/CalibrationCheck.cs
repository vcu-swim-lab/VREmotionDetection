using System;
using System.Linq;
using UnityEngine;
using UnityEngine.Events;

// A check on the headset, with the wearer's own Quest Pro face data: calibrate, then make
// each face again, and compare the model's own answer (uncalibrated) with the calibrated one
// on the very same predictions. With the Jev backend that compares Jev with Jev calibrated. With
// the local backend and a Jev Settings asset assigned, Jev's answer on the same window is added as
// a third column. Messages go to the console (adb logcat) and to `onMessage`, which you can
// connect to any text on screen. Nothing is saved except the usual results file.
[RequireComponent(typeof(NewFaceAuModel))]
public class CalibrationCheck : MonoBehaviour
{
    [Tooltip("Predictions per emotion in the test part (one per second).")]
    [SerializeField] private int predictionsPerEmotion = 5;

    [Tooltip("Connect to a text field to show the instructions and the result in the headset.")]
    public UnityEvent<string> onMessage;

    private NewFaceAuModel predictor;

    void Start()
    {
        predictor = GetComponent<NewFaceAuModel>();
        _ = Run();
    }

    private void Say(string message)
    {
        Debug.Log($"[CalibrationCheck] {message}");
        onMessage?.Invoke(message);
    }

    private async Awaitable Run()
    {
        var emotions = (Emotion[])Enum.GetValues(typeof(Emotion));

        // This check is about calibration, so it switches it on whatever the checkbox says.
        predictor.UseCalibration = true;

        // Part 1: calibrate.
        predictor.StartCalibration();
        foreach (var emotion in emotions)
        {
            Say($"Calibration: make a {emotion} face and hold it.");
            await Awaitable.WaitForSecondsAsync(2f);
            await predictor.CalibrateEmotion(emotion);
        }

        if (!predictor.FinishCalibration())
        {
            Say($"Calibration failed: {predictor.CalibrationError}");
            return;
        }

        // Part 2: the same predictions, read uncalibrated and calibrated.
        bool jevEnabled = predictor.JevSettings != null && predictor.Backend != PredictionBackend.Jev;
        int total = 0, valid = 0, rawCorrect = 0, calibratedCorrect = 0, jevCorrect = 0, changed = 0;
        var report = new System.Text.StringBuilder(
            "Emotion      uncalibrated  calibrated" + (jevEnabled ? "  jev" : "") + "\n");

        foreach (var emotion in emotions)
        {
            Say($"Test: make a {emotion} face and hold it.");
            await Awaitable.WaitForSecondsAsync(3f);

            int raw = 0, calibrated = 0, jev = 0;
            for (int i = 0; i < predictionsPerEmotion; ++i)
            {
                float next = Time.time + 1f;
                var prediction = await predictor.PredictDetailed();
                total++;

                if (prediction.HasValue && IsValid(prediction.Value.Scores))
                {
                    valid++;
                    var model = prediction.Value.Emotion;
                    var calibratedEmotion = predictor.CalibratedEmotion(prediction.Value) ?? model;
                    if (model == emotion) raw++;
                    if (calibratedEmotion == emotion) calibrated++;
                    if (calibratedEmotion != model) changed++;

                    if (jevEnabled)
                    {
                        var jevScores = await predictor.ScoreWithJev(prediction.Value.Window);
                        if (jevScores != null && Array.IndexOf(jevScores, jevScores.Max()) == (int)emotion) jev++;
                    }
                }

                float wait = next - Time.time;
                if (wait > 0) await Awaitable.WaitForSecondsAsync(wait);
            }

            rawCorrect += raw;
            calibratedCorrect += calibrated;
            jevCorrect += jev;
            report.AppendLine($"{emotion,-12} {raw}/{predictionsPerEmotion,-12} {calibrated}/{predictionsPerEmotion}" +
                              (jevEnabled ? $"  {jev}/{predictionsPerEmotion}" : ""));
        }

        report.AppendLine($"All          {rawCorrect}/{total,-12} {calibratedCorrect}/{total}" +
                          (jevEnabled ? $"  {jevCorrect}/{total}" : ""));
        report.AppendLine($"Valid predictions: {valid}/{total}. Calibration changed {changed} answers.");
        Say(report.ToString());
    }

    // Seven probabilities between 0 and 1 that sum to 1.
    private static bool IsValid(float[] scores) =>
        scores != null && scores.Length == 7 && scores.All(s => s >= 0f && s <= 1f) &&
        Math.Abs(scores.Sum() - 1f) < 1e-3f;
}
