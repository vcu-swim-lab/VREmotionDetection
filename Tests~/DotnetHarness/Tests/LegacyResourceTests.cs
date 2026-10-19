using System.Threading.Tasks;
using NUnit.Framework;
using Unity.InferenceEngine;
using UnityEngine;
using VcuSwimLab.VREmotionDetection;

// The obsolete FaceAuModel and the audio SoundModel create input tensors themselves, so they must dispose them once the
// inference is done - and must leave tensors that belong to the caller alone. Tensor.Live counts tensors that were
// created and not disposed.
#pragma warning disable CS0618 // FaceAuModel is obsolete on purpose
public class LegacyResourceTests
{
    private static ModelAsset Asset(string name) => new ModelAsset { name = name };

    private static async Task<(HarnessRig rig, FaceAuModel legacy)> LegacyFaceModel()
    {
        var rig = new HarnessRig();
        var legacy = rig.Host.AddComponent<FaceAuModel>();
        HarnessRig.Field(legacy, "naturalModel", Asset("natural"));
        HarnessRig.Field(legacy, "actedModel", Asset("acted"));
        legacy.Start();
        rig.Show(0);
        await rig.Settle();
        return (rig, legacy);
    }

    [Test]
    public async Task FaceAuModel_PredictRaw_disposes_the_input_window_and_leaves_only_the_two_outputs_to_the_caller()
    {
        var (rig, legacy) = await LegacyFaceModel();
        using (rig)
        {
            int before = Tensor.Live;

            var (natural, acted) = await legacy.PredictRaw();

            Assert.AreEqual(before + 2, Tensor.Live, "the input tensor must be disposed; only the two outputs belong to the caller");
            natural.Dispose();
            acted.Dispose();
            Assert.AreEqual(before, Tensor.Live);
        }
    }

    [Test]
    public async Task FaceAuModel_Predict_leaves_no_tensor_behind()
    {
        var (rig, legacy) = await LegacyFaceModel();
        using (rig)
        {
            int before = Tensor.Live;

            for (int i = 0; i < 3; ++i)
            {
                await legacy.Predict();
            }

            Assert.AreEqual(before, Tensor.Live, "repeated predictions must not accumulate tensors");
        }
    }

    private static SoundModel SoundModelReady()
    {
        var go = new GameObject("sound-model");
        var model = go.AddComponent<SoundModel>();
        HarnessRig.Field(model, "logmelVoiceModel", Asset("logmel"));
        HarnessRig.Field(model, "voiceModel", Asset("voice"));
        HarnessRig.Call(model, "Start");
        Worker.Canned = HarnessRig.Scores(2);
        return model;
    }

    [Test]
    public async Task SoundModel_Predict_of_a_clip_disposes_the_audio_tensor_it_prepared()
    {
        var model = SoundModelReady();
        int before = Tensor.Live;

        for (int i = 0; i < 3; ++i)
        {
            var (emotion, confidence) = await model.Predict(new AudioClip());
            Assert.AreEqual(Emotion.Fear, emotion);
        }

        Assert.AreEqual(before, Tensor.Live, "repeated clip predictions must not accumulate tensors");
    }

    [Test]
    public async Task SoundModel_Predict_does_not_dispose_a_tensor_the_caller_passed_in()
    {
        var model = SoundModelReady();
        using var input = SoundModel.PrepareAudio(new AudioClip());

        await model.Predict(input);

        Assert.IsFalse(input.IsDisposed, "the caller owns it");
    }
}
#pragma warning restore CS0618
