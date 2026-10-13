using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

// Saves every prediction of one session to a CSV file on the device, and keeps a running
// summary. One file per session (one run of the app), opened at the first prediction.
//
// Columns: time_utc, seconds, emotion, confidence, calibrated, model_emotion.
// `emotion` is what the game received (calibrated when calibration is on); `model_emotion`
// is the model's own answer. Raw face-tracking values are not saved.
//
// Files: Application.persistentDataPath/VREmotionDetection/results/session_<time>.csv. On the
// headset, Unity's menu "VR Emotion > Get Results From Headset" copies them to the PC.
internal sealed class ResultsRecorder : IDisposable
{
    public const string FolderName = "results";

    private readonly int[] counts = new int[Enum.GetValues(typeof(Emotion)).Length];
    private readonly double[] confidenceSums = new double[Enum.GetValues(typeof(Emotion)).Length];
    private int noTracking;

    private StreamWriter writer;
    private DateTime? startedUtc;

    public string FilePath { get; private set; }

    public static string Folder =>
        Path.Combine(Application.persistentDataPath, "VREmotionDetection", FolderName);

    public void Record(Emotion emotion, float confidence, bool calibrated, Emotion modelEmotion)
    {
        counts[(int)emotion]++;
        confidenceSums[(int)emotion] += confidence;

        Write(string.Join(",",
            Stamp(),
            emotion.ToString(),
            confidence.ToString("0.0000", CultureInfo.InvariantCulture),
            calibrated ? "yes" : "no",
            modelEmotion.ToString()));
    }

    // A prediction that could not be made because face tracking gave no valid frames.
    public void RecordNoTracking()
    {
        noTracking++;
        Write(string.Join(",", Stamp(), "no_tracking", "", "", ""));
    }

    public string Summary()
    {
        int total = counts.Sum();
        if (total == 0)
        {
            return noTracking > 0
                ? "No emotions recorded: face tracking was not available."
                : "No emotions recorded yet.";
        }

        // Invariant culture: the same "0.75" on every device, whatever its language settings.
        var inv = CultureInfo.InvariantCulture;
        var lines = new List<string>
        {
            string.Format(inv, "{0} predictions over {1:0.0} min", total, Seconds() / 60.0),
        };

        foreach (int e in Enumerable.Range(0, counts.Length).OrderByDescending(e => counts[e]))
        {
            if (counts[e] == 0) continue;
            lines.Add(string.Format(inv, "{0}: {1:0}% (average confidence {2:0.00})",
                                    (Emotion)e, 100.0 * counts[e] / total, confidenceSums[e] / counts[e]));
        }

        if (noTracking > 0)
        {
            lines.Add(string.Format(inv, "No face tracking: {0} of {1} predictions",
                                    noTracking, total + noTracking));
        }

        return string.Join("\n", lines);
    }

    // Seconds since the session's first prediction.
    private double Seconds() => startedUtc.HasValue ? (DateTime.UtcNow - startedUtc.Value).TotalSeconds : 0;

    // "time_utc,seconds" for a new row; the first row starts the session clock.
    private string Stamp()
    {
        var now = DateTime.UtcNow;
        startedUtc ??= now;
        return now.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture) + "," +
               (now - startedUtc.Value).TotalSeconds.ToString("0.000", CultureInfo.InvariantCulture);
    }

    private void Write(string line)
    {
        try
        {
            if (writer == null)
            {
                bool created = FilePath == null;
                if (created)
                {
                    Directory.CreateDirectory(Folder);
                    FilePath = Path.Combine(Folder,
                        "session_" + startedUtc.Value.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture) + ".csv");
                }

                // No BOM, so Excel, pandas and csv readers all see the first column's real name.
                writer = new StreamWriter(FilePath, append: true, new UTF8Encoding(false)) { AutoFlush = true };
                if (created)
                {
                    writer.WriteLine("time_utc,seconds,emotion,confidence,calibrated,model_emotion");
                }
            }

            writer.WriteLine(line);
        }
        catch (Exception e)
        {
            // Saving results must never stop the game.
            Debug.LogWarning($"VREmotionDetection: could not save results ({e.GetType().Name}: {e.Message})");
        }
    }

    // Closes the file; the next prediction reopens it and appends. Called when the app is
    // paused (the headset taken off), so nothing is lost if it is never resumed.
    public void Dispose()
    {
        writer?.Dispose();
        writer = null;
    }
}
