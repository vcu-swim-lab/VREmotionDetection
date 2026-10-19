#if NEW_API
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Unity.InferenceEngine;
using UnityEngine;
using VcuSwimLab.Backends;
using VcuSwimLab.Backends.Jev;
using VcuSwimLab.VREmotionDetection;

public class OrchestratorTests
{
    private const string Key = "harness-key-7f3a";

    private static readonly string[] Names = Enum.GetNames(typeof(Emotion));

    private sealed class ScriptedTransport : IHttpTransport
    {
        private int calls;

        public Func<string, (long status, string body)> Handler;

        public int Calls => calls;

        public List<string> Bodies { get; } = new List<string>();

        public async Awaitable<HttpReply> PostJsonAsync(string url, string bearerToken, string jsonBody, float timeoutSeconds)
        {
            Interlocked.Increment(ref calls);
            lock (Bodies) Bodies.Add(jsonBody);
            var (status, body) = Handler(jsonBody);
            return new HttpReply(status, body);
        }
    }

    private static string Answers(int peak, double value, double others = 0.0)
    {
        var answers = new JObject();
        for (int e = 0; e < 7; ++e)
        {
            answers[Names[e]] = new JObject { ["type"] = "noul", ["noul"] = e == peak ? value : others };
        }

        return new JObject { ["answers"] = answers }.ToString();
    }

    private static JevBackend JevWith(ScriptedTransport transport, int retries = 0, bool sendFaces = false)
    {
        var options = new JevOptions(Key) { MaxRetries = retries, SendOwnFacesInRequest = sendFaces };
        return new JevBackend(options, FaceAuNames.Movement, transport);
    }

    private static void AssertNoKeyLeaked()
    {
        lock (Debug.Warnings) CollectionAssert.IsEmpty(Debug.Warnings.Where(w => w.Contains(Key)));
        lock (Debug.Errors) CollectionAssert.IsEmpty(Debug.Errors.Where(w => w.Contains(Key)));
    }

    [Test]
    public async Task SparseVr_is_the_default_and_keeps_the_probability_as_confidence()
    {
        using var rig = new HarnessRig();
        rig.Show(3);
        Worker.Canned = HarnessRig.Scores(3);
        await rig.Settle();

        var (emotion, confidence) = await rig.Model.Predict();

        Assert.AreEqual("harness-model", rig.Model.ActiveBackendId);
        Assert.AreEqual(Emotion.Happiness, emotion);
        Assert.AreEqual(0.76f, confidence, 1e-6f);
        Assert.IsNull(rig.Model.LastError);
        Assert.AreEqual(0, Microphone.StartCalls);
    }

    [Test]
    public async Task Jev_returns_its_pick_with_the_raw_answer_as_confidence()
    {
        using var rig = new HarnessRig();
        var transport = new ScriptedTransport { Handler = _ => (200, Answers(5, 0.6)) };
        rig.Model.UseBackend(JevWith(transport));
        rig.Show(5);
        await rig.Settle();

        var (emotion, confidence) = await rig.Model.Predict();
        var prediction = (await rig.Model.PredictDetailed(saveResult: false)).Value;

        Assert.AreEqual(Emotion.Sadness, emotion);
        Assert.AreEqual(0.6f, confidence, 1e-6f);
        Assert.AreEqual(1f, prediction.Scores[5], 1e-5f);
        Assert.AreEqual(0.6f, prediction.Raw[5], 1e-6f);
        Assert.AreEqual("jev:jev-1.13.0:p1", prediction.BackendId);
        Assert.AreEqual(0, Microphone.StartCalls);
        AssertNoKeyLeaked();
    }

    [Test]
    public async Task A_rejected_key_gives_neutral_zero_a_typed_error_one_warning_and_backend_error_rows()
    {
        using var rig = new HarnessRig();
        var transport = new ScriptedTransport { Handler = _ => (401, "{\"detail\":\"Invalid key\"}") };
        rig.Model.UseBackend(JevWith(transport));
        var raised = new List<PredictionError>();
        rig.Model.PredictionFailed += raised.Add;
        await rig.Settle();

        for (int i = 0; i < 3; ++i)
        {
            var (emotion, confidence) = await rig.Model.Predict();
            Assert.AreEqual(Emotion.Neutral, emotion);
            Assert.AreEqual(0f, confidence);
        }

        Assert.AreEqual(PredictionErrorCode.Unauthorized, rig.Model.LastError.Value.Code);
        StringAssert.Contains("API key", rig.Model.LastError.Value.Message);
        Assert.AreEqual(3, raised.Count);
        lock (Debug.Warnings) Assert.AreEqual(1, Debug.Warnings.Count(w => w.Contains("Unauthorized")));
        var rows = File.ReadAllLines(rig.Model.ResultsFilePath);
        Assert.AreEqual(3, rows.Count(r => r.Contains(",backend_error,")));
        Assert.AreEqual(0, rows.Count(r => r.Contains("no_tracking")));
        Assert.AreEqual(3, transport.Calls);
        AssertNoKeyLeaked();

        transport.Handler = _ => (200, Answers(1, 0.7));
        await rig.Model.Predict();
        Assert.IsNull(rig.Model.LastError);
        StringAssert.Contains("Backend did not answer: 3 of 4 predictions", rig.Model.GetResultsSummary());
    }

    [Test]
    public async Task Transient_failures_are_retried_and_then_succeed()
    {
        using var rig = new HarnessRig();
        int call = 0;
        var transport = new ScriptedTransport { Handler = _ => ++call < 3 ? (503, "busy") : (200, Answers(2, 0.8)) };
        rig.Model.UseBackend(JevWith(transport, retries: 2));
        await rig.Settle();

        var (emotion, confidence) = await rig.Model.Predict();

        Assert.AreEqual(Emotion.Fear, emotion);
        Assert.AreEqual(0.8f, confidence, 1e-6f);
        Assert.AreEqual(3, transport.Calls);
    }

    [Test]
    public async Task A_network_outage_is_reported_as_unreachable_after_the_retries()
    {
        using var rig = new HarnessRig();
        var transport = new ScriptedTransport { Handler = _ => (0, "Cannot connect") };
        rig.Model.UseBackend(JevWith(transport, retries: 1));
        await rig.Settle();

        var outcome = await rig.Model.TryPredict(saveResult: false);

        Assert.IsFalse(outcome.Ok);
        Assert.AreEqual(PredictionErrorCode.Unreachable, outcome.Error.Value.Code);
        Assert.IsTrue(outcome.Error.Value.IsTransient);
        Assert.AreEqual(2, transport.Calls);
    }

    [Test]
    public async Task Predicting_before_start_is_a_typed_error_that_yields_a_frame()
    {
        using var rig = new HarnessRig(start: false);

        var outcome = await rig.Model.TryPredict(saveResult: false);
        var (emotion, confidence) = await rig.Model.Predict();

        Assert.AreEqual(PredictionErrorCode.NotReady, outcome.Error.Value.Code);
        Assert.AreEqual(Emotion.Neutral, emotion);
        Assert.AreEqual(0f, confidence);
    }

    [Test]
    public async Task Without_a_model_or_backend_the_error_says_what_to_do_and_jev_alone_still_works()
    {
        using var rig = new HarnessRig(withModel: false);
        await rig.Settle();

        var outcome = await rig.Model.TryPredict(saveResult: false);

        Assert.AreEqual(PredictionErrorCode.NotConfigured, outcome.Error.Value.Code);
        StringAssert.Contains("UseJev", outcome.Error.Value.Message);
        lock (Debug.Warnings) Assert.IsTrue(Debug.Warnings.Any(w => w.Contains("no face model is assigned")));
        Assert.Throws<InvalidOperationException>(() => rig.Model.UseSparseVr());

        rig.Model.UseBackend(JevWith(new ScriptedTransport { Handler = _ => (200, Answers(4, 0.9)) }));
        var (emotion, _) = await rig.Model.Predict();

        Assert.AreEqual(Emotion.Neutral, emotion);
        Assert.IsNull(await rig.Model.PredictRaw());
    }

    [Test]
    public async Task A_second_prediction_while_one_is_running_is_busy_not_corrupted()
    {
        using var rig = new HarnessRig();
        rig.Show(0, tracked: false);
        await rig.Settle();

        var first = rig.Model.TryPredict(1f, saveResult: false);
        await Task.Delay(100);
        var second = await rig.Model.TryPredict(1f, saveResult: false);
        var firstOutcome = await first;

        Assert.AreEqual(PredictionErrorCode.Busy, second.Error.Value.Code);
        Assert.AreEqual(PredictionErrorCode.NoTracking, firstOutcome.Error.Value.Code);

        rig.Show(0);
        await rig.Settle();
        Assert.IsTrue((await rig.Model.TryPredict(saveResult: false)).Ok);
    }

    [Test]
    public async Task Lost_tracking_is_a_no_tracking_error_recorded_as_such()
    {
        using var rig = new HarnessRig();
        rig.Show(0, tracked: false);
        await rig.Settle();

        var outcome = await rig.Model.TryPredict(0.3f);

        Assert.AreEqual(PredictionErrorCode.NoTracking, outcome.Error.Value.Code);
        Assert.IsNull(await rig.Model.PredictDetailed(0.3f, saveResult: false));
        StringAssert.EndsWith(",no_tracking,,,", File.ReadAllLines(rig.Model.ResultsFilePath).Last());
    }

    [Test]
    public void Bad_arguments_throw_instead_of_failing_quietly()
    {
        using var rig = new HarnessRig();

        Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () => await rig.Model.TryPredict(0f));
        Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () => await rig.Model.PredictDetailed(-1f));
        rig.Model.StartCalibration();
        Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () => await rig.Model.CalibrateEmotion(Emotion.Anger, -1f));
        Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () => await rig.Model.CalibrateEmotion(Emotion.Anger, 1f, 0));
        Assert.Throws<ArgumentNullException>(() => rig.Model.UseJev(null));
        Assert.Throws<ArgumentNullException>(() => rig.Model.UseBackend(null));
        Assert.Throws<ArgumentException>(() => rig.Model.LoadCalibration("../escape"));
    }

    [Test]
    public async Task Calibration_on_jev_uses_jevs_answers_saves_with_a_jev_id_and_is_refused_by_the_other_backend()
    {
        using var rig = new HarnessRig();
        int truth = 0;
        var transport = new ScriptedTransport { Handler = _ => (200, Answers(truth, 0.9)) };
        rig.Model.UseBackend(JevWith(transport));

        rig.Model.StartCalibration();
        for (int e = 0; e < 7; ++e)
        {
            truth = e;
            rig.Show(e);
            await rig.Settle();
            Assert.IsTrue(await rig.Model.CalibrateEmotion((Emotion)e, 0.001f), $"calibrating {(Emotion)e}");
        }

        Assert.IsTrue(rig.Model.FinishCalibration(), rig.Model.CalibrationError);
        Assert.IsTrue(rig.Model.IsCalibrated);
        Assert.GreaterOrEqual(transport.Calls, 70);

        for (int e = 0; e < 7; ++e)
        {
            truth = e;
            rig.Show(e);
            await rig.Settle();
            var (emotion, confidence) = await rig.Model.Predict();
            Assert.AreEqual((Emotion)e, emotion);
            Assert.AreEqual(0.9f, confidence, 1e-6f);
        }

        Assert.IsTrue(rig.Model.SaveCalibration("jevuser"));
        StringAssert.Contains("jev:jev-1.13.0:p1", File.ReadAllText(NewFaceAuModel.CalibrationPath("jevuser")));
        Assert.IsTrue(rig.Model.LoadCalibration("jevuser"), rig.Model.CalibrationError);

        rig.Model.UseSparseVr();
        Assert.IsFalse(rig.Model.IsCalibrated);
        Assert.IsFalse(rig.Model.LoadCalibration("jevuser"));
        StringAssert.Contains("calibrate again", rig.Model.CalibrationError);
        AssertNoKeyLeaked();
    }

    [Test]
    public async Task Own_faces_reach_jev_only_when_requested_and_calibrated()
    {
        using var rig = new HarnessRig();
        int truth = 0;
        var transport = new ScriptedTransport { Handler = _ => (200, Answers(truth, 0.9)) };
        rig.Model.UseBackend(JevWith(transport, sendFaces: true));

        rig.Show(0);
        await rig.Settle();
        await rig.Model.TryPredict(saveResult: false);
        Assert.IsNull(JObject.Parse(transport.Bodies.Last())["state"]["players_own_faces"]);

        rig.Model.StartCalibration();
        for (int e = 0; e < 7; ++e)
        {
            truth = e;
            rig.Show(e);
            await rig.Settle();
            Assert.IsTrue(await rig.Model.CalibrateEmotion((Emotion)e, 0.001f));
        }

        Assert.IsTrue(rig.Model.FinishCalibration());
        await rig.Model.TryPredict(saveResult: false);
        Assert.IsNotNull(JObject.Parse(transport.Bodies.Last())["state"]["players_own_faces"]);
    }

    [Test]
    public async Task Calibrate_all_prompts_each_emotion_in_order_and_finishes()
    {
        using var rig = new HarnessRig();
        var prompted = new List<Emotion>();

        bool calibrated = await rig.Model.CalibrateAll(async emotion =>
        {
            prompted.Add(emotion);
            rig.Show((int)emotion);
            Worker.Canned = HarnessRig.Scores((int)emotion);
            await Awaitable.WaitForSecondsAsync(0.05f);
        }, secondsBetweenSamples: 0.001f);

        Assert.IsTrue(calibrated, rig.Model.CalibrationError);
        Assert.IsTrue(rig.Model.IsCalibrated);
        CollectionAssert.AreEqual(Enum.GetValues(typeof(Emotion)).Cast<Emotion>().ToArray(), prompted);
    }

    [Test]
    public async Task Calibration_calls_do_nothing_when_calibration_is_off()
    {
        using var rig = new HarnessRig(useCalibration: false);

        Assert.DoesNotThrow(() => rig.Model.BeginCalibrationEmotion(Emotion.Anger));
        Assert.IsFalse(rig.Model.AddCalibrationSample(0, null));
        Assert.IsFalse(await rig.Model.CalibrateEmotion(Emotion.Anger, 0.001f));
        Assert.IsFalse(await rig.Model.CalibrateAll());
        Assert.IsFalse(rig.Model.FinishCalibration());
        Assert.IsFalse(rig.Model.LoadCalibration("anyone"));
        Assert.IsNull(rig.Model.CalibrationSampleCounts());
    }

    [Test]
    public void Calibration_calls_in_the_wrong_order_throw()
    {
        using var rig = new HarnessRig();

        Assert.Throws<InvalidOperationException>(() => rig.Model.BeginCalibrationEmotion(Emotion.Anger));
        Assert.Throws<InvalidOperationException>(() => rig.Model.AddCalibrationSample(1, null));
        Assert.ThrowsAsync<InvalidOperationException>(async () => await rig.Model.CalibrateEmotion(Emotion.Anger));
    }

    [Test]
    public async Task Switching_the_backend_during_a_calibration_stops_it_with_an_explanation_but_no_prediction_error()
    {
        using var rig = new HarnessRig();
        rig.Model.StartCalibration();
        rig.Show(0);
        Worker.Canned = HarnessRig.Scores(0);
        await rig.Settle();
        Worker.ReadbackDelayMs = 400;

        var running = rig.Model.CalibrateEmotion(Emotion.Anger, 0.001f);
        await Task.Delay(100);
        rig.Model.UseBackend(JevWith(new ScriptedTransport { Handler = _ => (200, Answers(0, 0.9)) }));

        Assert.IsFalse(await running);
        Assert.IsFalse(rig.Model.IsCalibrated);
        Assert.IsNull(rig.Model.CalibrationSampleCounts());
        // Since 3.0.0 the calibration says why it stopped; the interrupted prediction itself is not an error.
        StringAssert.Contains("interrupted", rig.Model.CalibrationError);
        Assert.IsNull(rig.Model.LastError);
    }

    [Test]
    public async Task A_calibration_file_written_by_the_released_build_loads()
    {
        using var rig = new HarnessRig(modelName: "golden-model");
        Directory.CreateDirectory(Path.GetDirectoryName(NewFaceAuModel.CalibrationPath("released")));
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Fixtures", "released_calibration.json"),
                  NewFaceAuModel.CalibrationPath("released"));

        Assert.IsTrue(rig.Model.LoadCalibration("released"), rig.Model.CalibrationError);
        Assert.IsTrue(rig.Model.IsCalibrated);

        rig.Show(2);
        Worker.Canned = HarnessRig.Scores(0);
        await rig.Settle();
        var prediction = (await rig.Model.PredictDetailed(saveResult: false)).Value;
        Assert.AreEqual(Emotion.Fear, rig.Model.CalibratedEmotion(prediction));
    }

    [Test]
    public async Task Disabling_and_enabling_again_restores_prediction()
    {
        using var rig = new HarnessRig();
        rig.Show(1);
        Worker.Canned = HarnessRig.Scores(1);
        await rig.Settle();
        Assert.IsTrue((await rig.Model.TryPredict(saveResult: false)).Ok);

        HarnessRig.Call(rig.Model, "OnDisable");
        Assert.IsNull(rig.Model.ActiveBackendId);
        Assert.AreEqual(PredictionErrorCode.NotReady, (await rig.Model.TryPredict(saveResult: false)).Error.Value.Code);

        HarnessRig.Call(rig.Model, "OnEnable");
        await rig.Settle();
        var outcome = await rig.Model.TryPredict(saveResult: false);

        Assert.IsTrue(outcome.Ok);
        Assert.AreEqual("harness-model", rig.Model.ActiveBackendId);
    }

    [Test]
    public async Task Predict_raw_returns_the_sparse_vr_tensor_and_scoring_with_another_backend_leaves_the_active_one_alone()
    {
        using var rig = new HarnessRig();
        rig.Show(4);
        Worker.Canned = HarnessRig.Scores(4);
        await rig.Settle();

        using (var raw = await rig.Model.PredictRaw())
        {
            Assert.IsNotNull(raw);
            Assert.AreEqual(0.76f, raw.DownloadToArray()[4], 1e-6f);
        }

        var other = rig.Model.CreateJevBackend(new JevOptions(Key));
        Assert.AreEqual("jev:jev-1.13.0:p1", other.Id);
        var outcome = await rig.Model.TryPredict(saveResult: false);
        var scored = await rig.Model.ScoreWith(JevWith(new ScriptedTransport { Handler = _ => (200, Answers(6, 0.5)) }),
                                               outcome.Prediction.Window);

        Assert.IsTrue(scored.Ok);
        Assert.AreEqual(1f, scored.Scores[6], 1e-5f);
        Assert.AreEqual("harness-model", rig.Model.ActiveBackendId);
        await Task.Run(() => Assert.ThrowsAsync<ArgumentNullException>(async () => await rig.Model.ScoreWith(null, null)));
    }

    [Test]
    public async Task A_failing_event_handler_does_not_break_prediction()
    {
        using var rig = new HarnessRig();
        rig.Model.UseBackend(JevWith(new ScriptedTransport { Handler = _ => (401, "{}") }));
        rig.Model.PredictionFailed += _ => throw new InvalidOperationException("handler bug");
        await rig.Settle();

        var outcome = await rig.Model.TryPredict(saveResult: false);

        Assert.AreEqual(PredictionErrorCode.Unauthorized, outcome.Error.Value.Code);
        lock (Debug.Warnings) Assert.IsTrue(Debug.Warnings.Any(w => w.Contains("handler threw")));
    }

    private sealed class ScriptedBackend : IEmotionBackend
    {
        public Func<BackendRequest, BackendResult> Score = _ => throw new InvalidOperationException("not scripted");

        public List<CalibrationProfileSeen> Seen { get; } = new List<CalibrationProfileSeen>();

        public string Id { get; set; } = "scripted";

        public async Awaitable<BackendResult> ScoreAsync(BackendRequest request)
        {
            Seen.Add(new CalibrationProfileSeen(request.ReferenceProfile));
            return Score(request);
        }

        public float[] PrepareForCalibration(float[] scores) => scores;

        public void Dispose() { }
    }

    private readonly struct CalibrationProfileSeen
    {
        public CalibrationProfileSeen(VcuSwimLab.Calibration.CalibrationProfile profile) { Profile = profile; }

        public VcuSwimLab.Calibration.CalibrationProfile Profile { get; }
    }

    [Test]
    public async Task A_backend_that_throws_becomes_an_internal_error_not_an_exception()
    {
        using var rig = new HarnessRig();
        rig.Model.UseBackend(new ScriptedBackend { Score = _ => throw new InvalidOperationException("backend bug") });
        var raised = new List<PredictionError>();
        rig.Model.PredictionFailed += raised.Add;
        await rig.Settle();

        var outcome = await rig.Model.TryPredict(saveResult: false);
        var (emotion, confidence) = await rig.Model.Predict();

        Assert.AreEqual(PredictionErrorCode.Internal, outcome.Error.Value.Code);
        StringAssert.Contains("backend bug", outcome.Error.Value.Message);
        Assert.AreEqual("scripted", outcome.Error.Value.BackendId);
        Assert.AreEqual((Emotion.Neutral, 0f), (emotion, confidence));
        Assert.AreEqual(2, raised.Count);
        Assert.IsTrue((await rig.Model.TryPredict(saveResult: false)).Error.HasValue);
    }

    [Test]
    public async Task A_backend_that_returns_nothing_is_an_invalid_response()
    {
        using var rig = new HarnessRig();
        rig.Model.UseBackend(new ScriptedBackend { Score = _ => default });
        await rig.Settle();

        var outcome = await rig.Model.TryPredict(saveResult: false);

        Assert.AreEqual(PredictionErrorCode.InvalidResponse, outcome.Error.Value.Code);
    }

    [Test]
    public async Task A_backend_that_returns_the_wrong_number_of_scores_is_an_invalid_response()
    {
        using var rig = new HarnessRig();
        rig.Model.UseBackend(new ScriptedBackend { Score = _ => BackendResult.Success(new[] { 1f, 0f }) });
        await rig.Settle();

        var outcome = await rig.Model.TryPredict(saveResult: false);

        Assert.AreEqual(PredictionErrorCode.InvalidResponse, outcome.Error.Value.Code);
        StringAssert.Contains("expected 7", outcome.Error.Value.Message);
    }

    [Test]
    public async Task Scoring_with_a_backend_that_throws_returns_a_failure()
    {
        using var rig = new HarnessRig();
        var broken = new ScriptedBackend { Id = "broken", Score = _ => throw new InvalidOperationException("boom") };

        var result = await rig.Model.ScoreWith(broken, new[] { new float[70] });

        Assert.IsFalse(result.Ok);
        Assert.AreEqual(PredictionErrorCode.Internal, result.Error.Value.Code);
        Assert.AreEqual("broken", result.Error.Value.BackendId);
    }

    [Test]
    public async Task Predict_raw_declines_while_a_prediction_is_running()
    {
        using var rig = new HarnessRig();
        rig.Show(0, tracked: false);
        await rig.Settle();

        var running = rig.Model.TryPredict(1f, saveResult: false);
        await Task.Delay(100);
        var raw = await rig.Model.PredictRaw();
        await running;

        Assert.IsNull(raw);
        rig.Show(0);
        await rig.Settle();
        using (var tensor = await rig.Model.PredictRaw())
        {
            Assert.IsNotNull(tensor);
        }
    }

    [Test]
    public async Task The_calibration_summary_is_built_once_per_calibration_not_once_per_prediction()
    {
        using var rig = new HarnessRig();
        var backend = new ScriptedBackend
        {
            Score = request =>
            {
                var scores = Enumerable.Repeat(0.04f, 7).ToArray();
                scores[0] = 0.76f;
                return BackendResult.Success(scores);
            },
        };
        rig.Model.UseBackend(backend);

        async Task Calibrate()
        {
            rig.Model.StartCalibration();
            for (int e = 0; e < 7; ++e)
            {
                rig.Show(e);
                await rig.Settle();
                Assert.IsTrue(await rig.Model.CalibrateEmotion((Emotion)e, 0.001f));
            }

            Assert.IsTrue(rig.Model.FinishCalibration());
        }

        await Calibrate();
        backend.Seen.Clear();
        await rig.Model.TryPredict(saveResult: false);
        await rig.Model.TryPredict(saveResult: false);
        var first = backend.Seen[0].Profile;

        Assert.IsNotNull(first);
        Assert.AreSame(first, backend.Seen[1].Profile);

        await Calibrate();
        backend.Seen.Clear();
        await rig.Model.TryPredict(saveResult: false);

        Assert.AreNotSame(first, backend.Seen[0].Profile);
    }

    private static void PlaceReleasedCalibration(string userId)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(NewFaceAuModel.CalibrationPath(userId)));
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Fixtures", "released_calibration.json"),
                  NewFaceAuModel.CalibrationPath(userId));
    }

    [Test]
    public async Task A_calibration_loaded_before_the_predictor_starts_survives_its_start()
    {
        using var rig = new HarnessRig(start: false, modelName: "golden-model");
        PlaceReleasedCalibration("early");

        Assert.IsTrue(rig.Model.LoadCalibration("early"), rig.Model.CalibrationError);
        Assert.IsTrue(rig.Model.IsCalibrated);

        rig.Model.Start();

        Assert.IsTrue(rig.Model.IsCalibrated);
        Assert.AreEqual("golden-model", rig.Model.ActiveBackendId);
        rig.Show(2);
        Worker.Canned = HarnessRig.Scores(0);
        await rig.Settle();
        var prediction = (await rig.Model.PredictDetailed(saveResult: false)).Value;
        Assert.AreEqual(Emotion.Fear, rig.Model.CalibratedEmotion(prediction));
    }

    [Test]
    public void A_calibration_started_before_the_predictor_starts_is_kept()
    {
        using var rig = new HarnessRig(start: false);

        rig.Model.StartCalibration();
        rig.Model.Start();

        Assert.IsNotNull(rig.Model.CalibrationSampleCounts());
    }

    [Test]
    public void A_saved_calibration_can_be_copied_before_the_predictor_starts()
    {
        using var rig = new HarnessRig(start: false, modelName: "golden-model");
        PlaceReleasedCalibration("source");
        Assert.IsTrue(rig.Model.LoadCalibration("source"));

        Assert.IsTrue(rig.Model.SaveCalibration("copy"));

        StringAssert.Contains("\"faceModel\":\"golden-model\"", File.ReadAllText(NewFaceAuModel.CalibrationPath("copy")));
    }

    [Test]
    public void Without_a_model_or_backend_a_saved_calibration_cannot_be_matched()
    {
        using var rig = new HarnessRig(start: false, withModel: false);

        Assert.IsFalse(rig.Model.LoadCalibration("anyone"));
        StringAssert.Contains("no backend is selected", rig.Model.CalibrationError);
        Assert.IsFalse(rig.Model.SaveCalibration("anyone"));
    }

    [Test]
    public void Choosing_jev_after_loading_a_sparse_vr_calibration_clears_it_and_refuses_that_file()
    {
        using var rig = new HarnessRig(modelName: "golden-model");
        PlaceReleasedCalibration("sparse");
        Assert.IsTrue(rig.Model.LoadCalibration("sparse"));

        rig.Model.UseBackend(JevWith(new ScriptedTransport { Handler = _ => (200, Answers(0, 0.9)) }));

        Assert.IsFalse(rig.Model.IsCalibrated);
        Assert.IsFalse(rig.Model.LoadCalibration("sparse"));
        StringAssert.Contains("calibrate again", rig.Model.CalibrationError);
    }

    [Test]
    public async Task Choosing_jev_before_start_then_loading_a_jev_calibration_works_across_start()
    {
        int truth = 0;
        using (var first = new HarnessRig())
        {
            first.Model.UseBackend(JevWith(new ScriptedTransport { Handler = _ => (200, Answers(truth, 0.9)) }));
            first.Model.StartCalibration();
            for (int e = 0; e < 7; ++e)
            {
                truth = e;
                first.Show(e);
                await first.Settle();
                Assert.IsTrue(await first.Model.CalibrateEmotion((Emotion)e, 0.001f));
            }

            Assert.IsTrue(first.Model.FinishCalibration());
            Assert.IsTrue(first.Model.SaveCalibration("jevsaved"));
        }

        string folder = Application.persistentDataPath;
        using var second = new HarnessRig(start: false, storageFolder: folder);
        second.Model.UseBackend(JevWith(new ScriptedTransport { Handler = _ => (200, Answers(truth, 0.9)) }));

        Assert.IsTrue(second.Model.LoadCalibration("jevsaved"), second.Model.CalibrationError);
        second.Model.Start();

        Assert.IsTrue(second.Model.IsCalibrated);
        Assert.AreEqual("jev:jev-1.13.0:p1", second.Model.ActiveBackendId);
    }

    [Test]
    public async Task A_prediction_asked_for_before_the_predictor_starts_waits_for_it_to_start()
    {
        using var rig = new HarnessRig(start: false);
        rig.Show(3);
        Worker.Canned = HarnessRig.Scores(3);
        await rig.Settle();

        Awaitable.FrameDelayMs = 60;
        try
        {
            var pending = rig.Model.TryPredict(saveResult: false);
            rig.Model.Start();
            var outcome = await pending;

            Assert.IsTrue(outcome.Ok, outcome.Error?.ToString());
            Assert.AreEqual(Emotion.Happiness, outcome.Prediction.Emotion);
        }
        finally
        {
            Awaitable.FrameDelayMs = 1;
        }
    }
}
#endif
