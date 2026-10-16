using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using VcuSwimLab.Backends;

public class ResultsRecorderTests
{
    private string folder;

    [SetUp]
    public void SetUp()
    {
        folder = Path.Combine(Path.GetTempPath(), "results-" + Guid.NewGuid().ToString("N"));
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(folder))
        {
            Directory.Delete(folder, true);
        }
    }

    [Test]
    public void A_session_file_starts_with_the_documented_header_and_formats_rows()
    {
        using (var recorder = new ResultsRecorder(folder))
        {
            recorder.Record(Emotion.Happiness, 0.81234f, true, Emotion.Surprise);
            recorder.Record(Emotion.Neutral, 0.5f, false, Emotion.Neutral);

            var lines = File.ReadAllLines(recorder.FilePath);

            Assert.AreEqual("time_utc,seconds,emotion,confidence,calibrated,model_emotion", lines[0]);
            StringAssert.EndsWith(",Happiness,0.8123,yes,Surprise", lines[1]);
            StringAssert.EndsWith(",Neutral,0.5000,no,Neutral", lines[2]);
            StringAssert.IsMatch(@"^\d{4}-\d\d-\d\dT\d\d:\d\d:\d\d\.\d{3}Z,\d+\.\d{3},", lines[1]);
            StringAssert.IsMatch(@"session_\d{8}_\d{6}\.csv$", recorder.FilePath);
        }
    }

    [Test]
    public void Failures_are_recorded_by_kind_and_caller_mistakes_are_not()
    {
        using (var recorder = new ResultsRecorder(folder))
        {
            recorder.RecordFailure(PredictionErrorCode.NoTracking);
            recorder.RecordFailure(PredictionErrorCode.Unauthorized);
            recorder.RecordFailure(PredictionErrorCode.Timeout);
            recorder.RecordFailure(PredictionErrorCode.Busy);
            recorder.RecordFailure(PredictionErrorCode.NotReady);

            var lines = File.ReadAllLines(recorder.FilePath);

            Assert.AreEqual(4, lines.Length);
            StringAssert.EndsWith(",no_tracking,,,", lines[1]);
            StringAssert.EndsWith(",backend_error,,,", lines[2]);
            StringAssert.EndsWith(",backend_error,,,", lines[3]);
        }
    }

    [Test]
    public void Closing_for_a_pause_keeps_the_same_file_and_does_not_repeat_the_header()
    {
        var recorder = new ResultsRecorder(folder);
        recorder.Record(Emotion.Anger, 0.5f, false, Emotion.Anger);
        string path = recorder.FilePath;

        recorder.Close();
        recorder.Record(Emotion.Fear, 0.6f, false, Emotion.Fear);
        recorder.Dispose();

        var lines = File.ReadAllLines(path);
        Assert.AreEqual(3, lines.Length);
        Assert.AreEqual(1, lines.Count(l => l.StartsWith("time_utc")));
        Assert.AreEqual(path, recorder.FilePath);
    }

    [Test]
    public void After_dispose_nothing_is_written_or_reopened()
    {
        var recorder = new ResultsRecorder(folder);
        recorder.Record(Emotion.Anger, 0.5f, false, Emotion.Anger);
        string path = recorder.FilePath;
        recorder.Dispose();
        long size = new FileInfo(path).Length;

        recorder.Record(Emotion.Fear, 0.6f, false, Emotion.Fear);
        recorder.RecordFailure(PredictionErrorCode.NoTracking);

        Assert.AreEqual(size, new FileInfo(path).Length);
        using (var stream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            Assert.IsNotNull(stream);
        }
    }

    [Test]
    public void Recorders_started_together_never_share_a_file()
    {
        var recorders = Enumerable.Range(0, 5).Select(_ => new ResultsRecorder(folder)).ToArray();
        foreach (var recorder in recorders)
        {
            recorder.Record(Emotion.Anger, 0.5f, false, Emotion.Anger);
        }

        var paths = recorders.Select(r => r.FilePath).ToArray();
        foreach (var recorder in recorders)
        {
            recorder.Dispose();
        }

        Assert.AreEqual(5, paths.Distinct().Count());
        Assert.AreEqual(5, Directory.GetFiles(folder, "session_*.csv").Length);
    }

    [Test]
    public void The_summary_reports_shares_and_failures()
    {
        using (var recorder = new ResultsRecorder(folder))
        {
            StringAssert.Contains("No emotions recorded yet", recorder.Summary());

            recorder.RecordFailure(PredictionErrorCode.Unauthorized);
            StringAssert.Contains("the backend did not answer", recorder.Summary());

            recorder.Record(Emotion.Happiness, 0.9f, false, Emotion.Happiness);
            recorder.Record(Emotion.Happiness, 0.7f, false, Emotion.Happiness);
            recorder.RecordFailure(PredictionErrorCode.NoTracking);

            string summary = recorder.Summary();
            StringAssert.Contains("2 predictions over", summary);
            StringAssert.Contains("Happiness: 100% (average confidence 0.80)", summary);
            StringAssert.Contains("No face tracking: 1 of 4 predictions", summary);
            StringAssert.Contains("Backend did not answer: 1 of 4 predictions", summary);
        }
    }

    [Test]
    public void Saving_never_throws_when_the_folder_cannot_be_used()
    {
        string blocker = Path.Combine(Path.GetTempPath(), "results-blocker-" + Guid.NewGuid().ToString("N"));
        File.WriteAllText(blocker, "a file where a folder is needed");
        try
        {
            using (var recorder = new ResultsRecorder(Path.Combine(blocker, "inside")))
            {
                Assert.DoesNotThrow(() => recorder.Record(Emotion.Anger, 0.5f, false, Emotion.Anger));
            }
        }
        finally
        {
            File.Delete(blocker);
        }
    }
}
