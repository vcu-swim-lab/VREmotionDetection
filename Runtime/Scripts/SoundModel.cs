using System.Diagnostics;
using Unity.InferenceEngine;
using Unity.Collections;
using UnityEngine;
using System.Collections.Generic;

using Debug = UnityEngine.Debug;
using Mono.Cecil;

// [RequireComponent(typeof(DeviceManager))]
public class SoundModel : MonoBehaviour
{
    // logmel

    [SerializeField]
    private ModelAsset voiceModel;

    [SerializeField]
    private ModelAsset logmelVoiceModel;

    private Model voiceModelObject;
    private Model logmelVoiceModelObject;

    private Worker voiceWorker;
    private Worker logmelVoiceWorker;

    void Start()
    {
        Debug.Assert(voiceModel != null, $"Voice model not assigned in ${GetType().Name}");

        voiceModelObject = ModelLoader.Load(voiceModel);
        voiceWorker = new Worker(voiceModelObject, BackendType.CPU);

        logmelVoiceModelObject = ModelLoader.Load(logmelVoiceModel);
        logmelVoiceWorker = new Worker(logmelVoiceModelObject, BackendType.CPU);

        Debug.Log("SoundModel initialized.");
    }

    async Awaitable<Tensor<float>> Infer(Tensor<float> input)
    {
        logmelVoiceWorker.Schedule(input);

        var logMelTensor = logmelVoiceWorker.PeekOutput();

        voiceWorker.Schedule(logMelTensor);

        var voiceTensor = await voiceWorker.PeekOutput().ReadbackAndCloneAsync();
        return voiceTensor as Tensor<float>;
    }

    public async Awaitable<Tensor<float>> PredictRaw(Tensor<float> audioInput = null)
    {
        return await Infer(audioInput);
    }


    public async Awaitable<(Emotion, float)> Predict(Tensor<float> audioInput = null)
    {
        var voiceTensor = await PredictRaw(audioInput);
        var voiceArr = voiceTensor.AsReadOnlyNativeArray();

        int maxIndex = 0;
        float maxValue = voiceArr[0];
        for (int i = 1; i < voiceArr.Length; ++i)
        {
            if (voiceArr[i] > maxValue)
            {
                maxValue = voiceArr[i];
                maxIndex = i;
            }
        }

        voiceTensor.Dispose();

        return ((Emotion)maxIndex, maxValue);
    }

    public async Awaitable<(Emotion, float)> Predict(AudioClip clip)
    {
        var norm = PrepareAudio(clip);
        return await Predict(norm);
    }

    public void Dispose()
    {
        voiceWorker.Dispose();
    }

    public static Tensor<float> PrepareAudio(AudioClip originalClip)
    {
        // TODO(Terens): add resampling at some point in the future
        int targetLengthSeconds = 30;

        int channels = originalClip.channels;
        int frequency = originalClip.frequency;

        int targetSamples = targetLengthSeconds * frequency;

        // Get original samples
        float[] data = new float[targetSamples];
        originalClip.GetData(data, 0);

        return new Tensor<float>(new TensorShape(1, targetSamples), data);
    }
}
