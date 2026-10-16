using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using VcuSwimLab.Backends;

using Debug = UnityEngine.Debug;

internal sealed class ResultsRecorder : IDisposable
{
    public const string Header = "time_utc,seconds,emotion,confidence,calibrated,model_emotion";

    private readonly int[] counts = new int[Enum.GetValues(typeof(Emotion)).Length];
    private readonly double[] confidenceSums = new double[Enum.GetValues(typeof(Emotion)).Length];
    private readonly Stopwatch sessionClock = new Stopwatch();

    private int noTracking;
    private int backendErrors;
    private StreamWriter writer;
    private DateTime? startedUtc;
    private bool headerWritten;
    private bool disposed;

    public string FilePath { get; private set; }

    public static string Folder =>
        Path.Combine(Application.persistentDataPath, StoragePaths.Root, StoragePaths.Results);

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

    public void RecordFailure(PredictionErrorCode code)
    {
        switch (code)
        {
            case PredictionErrorCode.NoTracking:
                noTracking++;
                Write(string.Join(",", Stamp(), "no_tracking", "", "", ""));
                break;
            case PredictionErrorCode.NotReady:
            case PredictionErrorCode.NotConfigured:
            case PredictionErrorCode.Busy:
                break;
            default:
                backendErrors++;
                Write(string.Join(",", Stamp(), "backend_error", "", "", ""));
                break;
        }
    }

    public string Summary()
    {
        int total = counts.Sum();
        if (total == 0)
        {
            if (noTracking > 0)
            {
                return "No emotions recorded: face tracking was not available.";
            }

            return backendErrors > 0
                ? "No emotions recorded: the backend did not answer."
                : "No emotions recorded yet.";
        }

        var inv = CultureInfo.InvariantCulture;
        int attempts = total + noTracking + backendErrors;
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
            lines.Add(string.Format(inv, "No face tracking: {0} of {1} predictions", noTracking, attempts));
        }

        if (backendErrors > 0)
        {
            lines.Add(string.Format(inv, "Backend did not answer: {0} of {1} predictions", backendErrors, attempts));
        }

        return string.Join("\n", lines);
    }

    public void Close()
    {
        writer?.Dispose();
        writer = null;
    }

    public void Dispose()
    {
        Close();
        disposed = true;
    }

    private double Seconds() => startedUtc.HasValue ? sessionClock.Elapsed.TotalSeconds : 0;

    private string Stamp()
    {
        var now = DateTime.UtcNow;
        if (!startedUtc.HasValue)
        {
            startedUtc = now;
            sessionClock.Start();
        }

        return now.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture) + "," +
               sessionClock.Elapsed.TotalSeconds.ToString("0.000", CultureInfo.InvariantCulture);
    }

    private void Write(string line)
    {
        if (disposed)
        {
            return;
        }

        try
        {
            if (writer == null)
            {
                FilePath ??= ReservePath();
                writer = new StreamWriter(FilePath, append: true, new UTF8Encoding(false)) { AutoFlush = true };
            }

            if (!headerWritten)
            {
                writer.WriteLine(Header);
                headerWritten = true;
            }

            writer.WriteLine(line);
        }
        catch (Exception e)
        {
            Debug.LogWarning($"VREmotionDetection: could not save results ({e.GetType().Name}: {e.Message})");
        }
    }

    private string ReservePath()
    {
        string folder = Folder;
        Directory.CreateDirectory(folder);

        string stem = "session_" + startedUtc.Value.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
        string path = Path.Combine(folder, stem + ".csv");
        for (int suffix = 2; File.Exists(path); ++suffix)
        {
            path = Path.Combine(folder, stem + "_" + suffix + ".csv");
        }

        return path;
    }
}
