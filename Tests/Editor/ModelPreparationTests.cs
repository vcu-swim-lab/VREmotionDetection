using NUnit.Framework;
using Unity.InferenceEngine;
using UnityEditor;

// Pins the per-model softmax decision made in `Utils.PrepareModel`.
//
// The face graphs already end in Softmax, so appending another squashes the output and
// leaves the reported confidence meaningless. `sound_model.onnx` ends in Gemm and emits
// raw logits, so it genuinely needs one. A single global choice cannot be right for both,
// and getting it wrong is silent -- argmax is unaffected either way. Hence these tests.
public class ModelPreparationTests
{
    private const string ModelDir = "Packages/com.vcu-swim-lab.vr-emotion-detection/Runtime/Models/";

    private static ModelAsset LoadAsset(string fileName)
    {
        var asset = AssetDatabase.LoadAssetAtPath<ModelAsset>(ModelDir + fileName);
        if (asset == null)
        {
            Assert.Ignore($"{fileName} is not available at {ModelDir}; skipping.");
        }
        return asset;
    }

    private static float[] RunOnZeros(Model model, TensorShape shape)
    {
        using var worker = new Worker(model, BackendType.CPU);
        using var input = new Tensor<float>(shape, new float[shape.length]);

        worker.Schedule(input);

        using var output = worker.PeekOutput().ReadbackAndClone() as Tensor<float>;
        return output.DownloadToArray();
    }

    private static void AssertIsProbabilityDistribution(float[] values, string what)
    {
        float sum = 0f;
        foreach (var v in values)
        {
            Assert.That(v, Is.GreaterThanOrEqualTo(0f), $"{what}: found a negative score.");
            sum += v;
        }

        Assert.That(sum, Is.EqualTo(1f).Within(1e-4f), $"{what}: scores should sum to 1.");
    }

    [Test]
    public void FaceModel_WithoutAppendedSoftmax_OutputsProbabilities()
    {
        var asset = LoadAsset("sparseVR_teacher_model_acted_only_vol-aug-k1_with-EmojiHero.onnx");

        var model = Utils.PrepareModel(asset, appendSoftmax: false);
        var scores = RunOnZeros(model, new TensorShape(1, 5, 70));

        Assert.That(scores.Length, Is.EqualTo(7), "The teacher should emit one score per class.");
        AssertIsProbabilityDistribution(scores, "teacher without appended softmax");
    }

    [Test]
    public void SoundModel_EmitsLogits_SoItNeedsTheAppendedSoftmax()
    {
        var asset = LoadAsset("sound_model.onnx");

        var raw = RunOnZeros(Utils.PrepareModel(asset, appendSoftmax: false), new TensorShape(1, 80, 3000));

        float sum = 0f;
        foreach (var v in raw)
        {
            sum += v;
        }

        Assert.That(sum, Is.Not.EqualTo(1f).Within(1e-3f),
            "sound_model.onnx is expected to emit raw logits, not a distribution.");
    }

    [Test]
    public void SoundModel_WithAppendedSoftmax_OutputsProbabilities()
    {
        var asset = LoadAsset("sound_model.onnx");

        var model = Utils.PrepareModel(asset, appendSoftmax: true);
        var scores = RunOnZeros(model, new TensorShape(1, 80, 3000));

        AssertIsProbabilityDistribution(scores, "sound model with appended softmax");
    }
}
