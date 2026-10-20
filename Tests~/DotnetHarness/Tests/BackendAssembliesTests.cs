#if NEW_API
using System;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using Unity.InferenceEngine;
using UnityEngine;
using VcuSeCenter.Backends;
using VcuSeCenter.Backends.SparseVr;
using VcuSeCenter.VREmotionDetection;

public class BackendAssembliesTests
{
    private static readonly string[] Names = Enum.GetNames(typeof(Emotion));

    private static float[][] Window(int frames = 5, int features = 70) =>
        Enumerable.Range(0, frames).Select(_ => new float[features]).ToArray();

    private static SparseVrBackend Backend() => new SparseVrBackend(new ModelAsset { name = "unit-model" });

    [SetUp]
    public void SetUp()
    {
        Worker.Source = null;
        Worker.Canned = HarnessRig.Scores(2);
        Worker.ReadbackDelayMs = 0;
    }

    [Test]
    public async Task A_valid_window_is_scored()
    {
        using var backend = Backend();

        var result = await backend.ScoreAsync(new BackendRequest(Window(), Names));

        Assert.IsTrue(result.Ok);
        Assert.AreEqual("unit-model", backend.Id);
        Assert.AreEqual(0.76f, result.Scores[2], 1e-6f);
        Assert.IsNull(result.Raw);
        CollectionAssert.AreEqual(result.Scores, backend.PrepareForCalibration(result.Scores));
    }

    [Test]
    public async Task A_model_with_the_wrong_number_of_outputs_is_an_invalid_response()
    {
        using var backend = Backend();
        Worker.Source = () => new[] { 0.5f, 0.5f };

        var result = await backend.ScoreAsync(new BackendRequest(Window(), Names));

        Assert.AreEqual(PredictionErrorCode.InvalidResponse, result.Error.Value.Code);
        StringAssert.Contains("expected 7", result.Error.Value.Message);
    }

    [Test]
    public async Task Non_finite_or_negative_outputs_are_invalid_responses()
    {
        using var backend = Backend();

        foreach (var bad in new[] { float.NaN, float.PositiveInfinity, -0.1f })
        {
            Worker.Source = () => new[] { 0.1f, 0.1f, bad, 0.1f, 0.1f, 0.1f, 0.1f };
            var result = await backend.ScoreAsync(new BackendRequest(Window(), Names));
            Assert.AreEqual(PredictionErrorCode.InvalidResponse, result.Error.Value.Code, bad.ToString());
        }
    }

    [Test]
    public async Task A_window_of_the_wrong_shape_is_invalid_input_and_never_reaches_the_model()
    {
        using var backend = Backend();
        int before = Worker.Runs;

        foreach (var window in new[] { Window(4), Window(6), Window(5, 69), Window(5, 71), null })
        {
            var result = await backend.ScoreAsync(new BackendRequest(window, Names));
            Assert.AreEqual(PredictionErrorCode.InvalidInput, result.Error.Value.Code);
        }

        var nan = Window();
        nan[2][3] = float.NaN;
        Assert.AreEqual(PredictionErrorCode.InvalidInput, (await backend.ScoreAsync(new BackendRequest(nan, Names))).Error.Value.Code);
        Assert.AreEqual(before, Worker.Runs);
    }

    [Test]
    public async Task A_disposed_backend_reports_not_ready_and_disposing_twice_is_safe()
    {
        var backend = Backend();
        backend.Dispose();
        backend.Dispose();

        var result = await backend.ScoreAsync(new BackendRequest(Window(), Names));

        Assert.AreEqual(PredictionErrorCode.NotReady, result.Error.Value.Code);
    }

    [Test]
    public async Task Two_overlapping_calls_do_not_share_the_worker()
    {
        using var backend = Backend();
        Worker.ReadbackDelayMs = 150;

        var first = backend.ScoreAsync(new BackendRequest(Window(), Names));
        await Task.Delay(30);
        var second = await backend.ScoreAsync(new BackendRequest(Window(), Names));

        Assert.AreEqual(PredictionErrorCode.Busy, second.Error.Value.Code);
        Assert.IsTrue((await first).Ok);
        Assert.IsTrue((await backend.ScoreAsync(new BackendRequest(Window(), Names))).Ok);
    }

    [Test]
    public async Task A_failure_inside_the_model_is_reported_not_thrown()
    {
        using var backend = Backend();
        Worker.Source = () => throw new InvalidOperationException("gpu lost");

        var result = await backend.ScoreAsync(new BackendRequest(Window(), Names));

        Assert.AreEqual(PredictionErrorCode.Internal, result.Error.Value.Code);
        StringAssert.Contains("gpu lost", result.Error.Value.Message);
        Worker.Source = null;
        Assert.IsTrue((await backend.ScoreAsync(new BackendRequest(Window(), Names))).Ok);
    }

    [Test]
    public void The_backend_needs_a_model_asset()
    {
        Assert.Throws<ArgumentNullException>(() => new SparseVrBackend(null));
        Assert.Throws<ArgumentNullException>(() => OnnxModelLoader.Prepare(null, false));
    }

    [Test]
    public void The_window_tensor_is_one_by_frames_by_features_in_oldest_first_order()
    {
        var window = Window(5, 70);
        for (int f = 0; f < 5; ++f) window[f][0] = f;

        using var tensor = WindowTensor.From(window);
        var data = tensor.DownloadToArray();

        Assert.AreEqual(new TensorShape(1, 5, 70), tensor.shape);
        Assert.AreEqual(350, data.Length);
        Assert.AreEqual(new[] { 0f, 1f, 2f, 3f, 4f }, Enumerable.Range(0, 5).Select(f => data[f * 70]).ToArray());
    }
}
#endif
