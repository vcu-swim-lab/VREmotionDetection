using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Unity.InferenceEngine;
using UnityEngine;
using VcuSwimLab.Backends;
using VcuSwimLab.VREmotionDetection;

// A prediction that is still waiting when the backend changes or the predictor is disabled ends as Canceled, and the
// predictor leaves no trace of it: no LastError, no PredictionFailed, no warning, no row in the results file.
public class PredictorCancellationTests
{
    private sealed class StubBackend : ICancelableBackend
    {
        private int cancelCalls;

        public StubBackend(string id) { Id = id; }

        public string Id { get; }

        public int CancelCalls => Volatile.Read(ref cancelCalls);

        public bool Disposed { get; private set; }

        public bool ThrowOnCancel;

        public Func<Awaitable<BackendResult>> OnScore;

        public async Awaitable<BackendResult> ScoreAsync(BackendRequest request) =>
            OnScore != null ? await OnScore() : BackendResult.Success(HarnessRig.Scores(0));

        public float[] PrepareForCalibration(float[] scores) => scores;

        public void CancelPending()
        {
            Interlocked.Increment(ref cancelCalls);
            if (ThrowOnCancel)
            {
                throw new InvalidOperationException("cancel failed");
            }
        }

        public void Dispose() { Disposed = true; }
    }

    private static async Task<HarnessRig> RigWithSlowLocalModel(int readbackDelayMs = 400)
    {
        var rig = new HarnessRig();
        rig.Show(0);
        Worker.Canned = HarnessRig.Scores(0);
        await rig.Settle();
        Worker.ReadbackDelayMs = readbackDelayMs;
        return rig;
    }

    private static int WarningCount()
    {
        lock (Debug.Warnings) return Debug.Warnings.Count;
    }

    [TearDown]
    public void TearDown()
    {
        Worker.ReadbackDelayMs = 0;
        Awaitable.FrameDelayMs = 1;
    }

    [Test]
    public async Task Switching_the_backend_mid_prediction_gives_Canceled_and_leaves_no_trace()
    {
        using var rig = await RigWithSlowLocalModel();
        var raised = new List<PredictionError>();
        rig.Model.PredictionFailed += raised.Add;
        int warningsBefore = WarningCount();

        var pending = rig.Model.TryPredict();
        await Task.Delay(100);
        rig.Model.UseBackend(new StubBackend("next"));
        var outcome = await pending;

        Assert.IsFalse(outcome.Ok);
        Assert.AreEqual(PredictionErrorCode.Canceled, outcome.Error.Value.Code);
        Assert.IsFalse(outcome.Error.Value.IsTransient);
        Assert.IsNull(rig.Model.LastError, "a cancel is not a failure of the backend");
        CollectionAssert.IsEmpty(raised);
        Assert.AreEqual(warningsBefore, WarningCount(), "no warning is logged for a cancel");
        Assert.IsFalse(Directory.Exists(NewFaceAuModel.ResultsFolder) && Directory.GetFiles(NewFaceAuModel.ResultsFolder).Length > 0,
                       "a canceled prediction is not a result");

        // The next prediction is on the new backend, and this time a row is written (so the check above means something).
        Worker.ReadbackDelayMs = 0;
        var next = await rig.Model.TryPredict();
        Assert.IsTrue(next.Ok);
        Assert.AreEqual("next", next.Prediction.BackendId);
        Assert.AreEqual(1, Directory.GetFiles(NewFaceAuModel.ResultsFolder).Length);
    }

    [Test]
    public async Task Disabling_the_predictor_mid_prediction_gives_Canceled_and_Predict_answers_neutral_zero()
    {
        using var rig = await RigWithSlowLocalModel();
        var raised = new List<PredictionError>();
        rig.Model.PredictionFailed += raised.Add;

        var pending = rig.Model.Predict();
        await Task.Delay(100);
        HarnessRig.Call(rig.Model, "OnDisable");
        var (emotion, confidence) = await pending;

        Assert.AreEqual(Emotion.Neutral, emotion);
        Assert.AreEqual(0f, confidence);
        Assert.IsNull(rig.Model.LastError);
        CollectionAssert.IsEmpty(raised);

        var again = await rig.Model.TryPredict(saveResult: false);
        Assert.AreEqual(PredictionErrorCode.NotReady, again.Error.Value.Code, "after the disable the predictor is simply not ready");
    }

    [Test]
    public async Task A_late_canceled_prediction_does_not_clear_the_busy_state_of_a_newer_one()
    {
        using var rig = await RigWithSlowLocalModel(readbackDelayMs: 300);
        var gate = new TaskCompletionSource<bool>();
        var held = new StubBackend("held")
        {
            OnScore = async () =>
            {
                await gate.Task;
                return BackendResult.Success(HarnessRig.Scores(1));
            },
        };

        var first = rig.Model.TryPredict(saveResult: false);
        await Task.Delay(80);
        rig.Model.UseBackend(held);
        var second = rig.Model.TryPredict(saveResult: false);
        await Task.Delay(100);

        try
        {
            var firstOutcome = await first;
            Assert.AreEqual(PredictionErrorCode.Canceled, firstOutcome.Error.Value.Code);

            // Bounded: if the old prediction wrongly freed the busy state, this one would be let in and wait on the gate too.
            var thirdTask = Task.Run(async () => await rig.Model.TryPredict(saveResult: false));
            Assert.AreSame(thirdTask, await Task.WhenAny(thirdTask, Task.Delay(3000)),
                           "a prediction refused as Busy must answer promptly; it was let in next to the running one instead");
            Assert.AreEqual(PredictionErrorCode.Busy, thirdTask.Result.Error.Value.Code,
                            "the second prediction is still running; the first one finishing must not make room for another");
        }
        finally
        {
            gate.TrySetResult(true);
        }

        Assert.IsTrue((await second).Ok);
    }

    [Test]
    public async Task A_NotConfigured_answer_that_is_overtaken_by_choosing_a_backend_is_Canceled()
    {
        Awaitable.FrameDelayMs = 100;
        using var rig = new HarnessRig(withModel: false);
        rig.Show(0);
        await Task.Delay(50);

        var pending = rig.Model.TryPredict(saveResult: false);
        rig.Model.UseBackend(new StubBackend("chosen"));
        var outcome = await pending;

        Assert.AreEqual(PredictionErrorCode.Canceled, outcome.Error.Value.Code);
        Assert.IsNull(rig.Model.LastError);
    }

    [Test]
    public async Task Without_a_backend_the_answer_is_NotConfigured_and_is_not_written_to_the_results_file()
    {
        using var rig = new HarnessRig(withModel: false);
        rig.Show(0);
        await rig.Settle();

        var outcome = await rig.Model.TryPredict();

        Assert.AreEqual(PredictionErrorCode.NotConfigured, outcome.Error.Value.Code);
        Assert.IsFalse(Directory.Exists(NewFaceAuModel.ResultsFolder) && Directory.GetFiles(NewFaceAuModel.ResultsFolder).Length > 0);
    }

    [Test]
    public async Task A_Busy_answer_that_is_overtaken_by_choosing_a_backend_is_Canceled()
    {
        Awaitable.FrameDelayMs = 150;
        using var rig = await RigWithSlowLocalModel(readbackDelayMs: 600);

        var running = rig.Model.TryPredict(saveResult: false);
        await Task.Delay(300);
        var refused = rig.Model.TryPredict(saveResult: false);
        rig.Model.UseBackend(new StubBackend("chosen"));

        Assert.AreEqual(PredictionErrorCode.Canceled, (await refused).Error.Value.Code);
        Assert.AreEqual(PredictionErrorCode.Canceled, (await running).Error.Value.Code);
    }

    // Documents current behaviour. The local model has ONE worker: if its abandoned inference is still reading back, the
    // next local prediction is refused by the backend itself (code Busy, "already scoring a window") until it finishes.
    // That is the worker being genuinely occupied, not the predictor's own busy state leaking across the switch.
    [Test]
    public async Task Returning_to_the_local_model_while_its_abandoned_inference_is_still_running_is_Busy_until_it_ends()
    {
        using var rig = await RigWithSlowLocalModel(readbackDelayMs: 500);

        var abandoned = rig.Model.TryPredict(saveResult: false);
        await Task.Delay(100);
        rig.Model.UseBackend(new StubBackend("away"));
        rig.Model.UseLocalModel();

        var tooSoon = await rig.Model.TryPredict(saveResult: false);
        Assert.IsFalse(tooSoon.Ok);
        Assert.AreEqual(PredictionErrorCode.Busy, tooSoon.Error.Value.Code);
        StringAssert.Contains("already scoring", tooSoon.Error.Value.Message);
        Assert.AreEqual(PredictionErrorCode.Canceled, (await abandoned).Error.Value.Code);

        Worker.ReadbackDelayMs = 0;
        Assert.IsTrue((await rig.Model.TryPredict(saveResult: false)).Ok, "once the old inference has finished the local model works again");
    }

    [Test]
    public async Task Leaving_a_cancelable_backend_cancels_and_disposes_it_exactly_once()
    {
        using var rig = new HarnessRig();
        rig.Show(0);
        await rig.Settle();
        var first = new StubBackend("first");
        var second = new StubBackend("second");

        rig.Model.UseBackend(first);
        rig.Model.UseBackend(first);
        Assert.AreEqual(0, first.CancelCalls, "choosing the active backend again changes nothing");

        rig.Model.UseBackend(second);

        Assert.AreEqual(1, first.CancelCalls);
        Assert.IsTrue(first.Disposed);
        Assert.AreEqual(0, second.CancelCalls);
        Assert.IsFalse(second.Disposed);
    }

    [Test]
    public async Task Disabling_the_predictor_cancels_the_active_backend()
    {
        using var rig = new HarnessRig();
        rig.Show(0);
        await rig.Settle();
        var active = new StubBackend("active");
        rig.Model.UseBackend(active);

        HarnessRig.Call(rig.Model, "OnDisable");

        Assert.AreEqual(1, active.CancelCalls);
    }

    [Test]
    public async Task A_backend_whose_CancelPending_throws_cannot_break_the_predictor()
    {
        using var rig = new HarnessRig();
        rig.Show(0);
        await rig.Settle();
        var thrower = new StubBackend("thrower") { ThrowOnCancel = true };
        rig.Model.UseBackend(thrower);

        Assert.DoesNotThrow(() => rig.Model.UseBackend(new StubBackend("next")));

        Assert.IsTrue(thrower.Disposed, "the old backend is still disposed");
        lock (Debug.Warnings) Assert.IsTrue(Debug.Warnings.Any(w => w.Contains("CancelPending threw")));
        Assert.IsTrue((await rig.Model.TryPredict(saveResult: false)).Ok);
    }

    [Test]
    public async Task A_backend_that_throws_while_being_canceled_is_Canceled_not_Internal()
    {
        using var rig = new HarnessRig();
        rig.Show(0);
        await rig.Settle();
        var gate = new TaskCompletionSource<bool>();
        rig.Model.UseBackend(new StubBackend("slow")
        {
            OnScore = async () =>
            {
                await gate.Task;
                throw new InvalidOperationException("the request was torn down");
            },
        });

        var pending = rig.Model.TryPredict(saveResult: false);
        await Task.Delay(100);
        rig.Model.UseBackend(new StubBackend("next"));
        gate.SetResult(true);
        var outcome = await pending;

        Assert.AreEqual(PredictionErrorCode.Canceled, outcome.Error.Value.Code);
        Assert.IsNull(rig.Model.LastError);
    }

    [Test]
    public async Task A_backend_that_throws_without_a_cancel_is_reported_as_Internal()
    {
        using var rig = new HarnessRig();
        rig.Show(0);
        await rig.Settle();
        rig.Model.UseBackend(new StubBackend("faulty") { OnScore = () => throw new InvalidOperationException("bug in the backend") });

        var outcome = await rig.Model.TryPredict(saveResult: false);

        Assert.AreEqual(PredictionErrorCode.Internal, outcome.Error.Value.Code);
        StringAssert.Contains("bug in the backend", outcome.Error.Value.Message);
        Assert.AreEqual(PredictionErrorCode.Internal, rig.Model.LastError.Value.Code);
    }

    [Test]
    public async Task A_backend_that_answers_with_Canceled_by_itself_is_not_reported_either()
    {
        using var rig = new HarnessRig();
        rig.Show(0);
        await rig.Settle();
        var raised = new List<PredictionError>();
        rig.Model.PredictionFailed += raised.Add;
        rig.Model.UseBackend(new StubBackend("self-canceling")
        {
            OnScore = async () => BackendResult.Failure(PredictionErrorCode.Canceled, "gave up", "self-canceling"),
        });

        var outcome = await rig.Model.TryPredict(saveResult: false);

        Assert.AreEqual(PredictionErrorCode.Canceled, outcome.Error.Value.Code);
        Assert.IsNull(rig.Model.LastError);
        CollectionAssert.IsEmpty(raised);
    }
}
