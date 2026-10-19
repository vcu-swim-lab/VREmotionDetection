using System.Diagnostics;
using Unity.InferenceEngine;
using VcuSwimLab.Backends.SparseVr;
using Unity.Collections;
using UnityEngine;
using System.Collections.Generic;

using Debug = UnityEngine.Debug;

namespace VcuSwimLab.VREmotionDetection
{
    public class SoundModel : MonoBehaviour
    {
        [SerializeField]
        private ModelAsset logmelVoiceModel;

        [SerializeField]
        private ModelAsset voiceModel;

        private Model logmelVoiceModelObject;
        private Model voiceModelObject;

        private Worker logmelVoiceWorker;
        private Worker voiceWorker;

        void Start()
        {
            if (logmelVoiceModel == null) Debug.LogError($"Logmel model not assigned in {GetType().Name}");
            if (voiceModel == null) Debug.LogError($"Voice model not assigned in {GetType().Name}");

            logmelVoiceModelObject = ModelLoader.Load(logmelVoiceModel);
            logmelVoiceWorker = new Worker(logmelVoiceModelObject, BackendType.GPUCompute);

            voiceModelObject = OnnxModelLoader.Prepare(voiceModel, appendSoftmax: true);
            voiceWorker = new Worker(voiceModelObject, BackendType.GPUCompute);

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
            using var voiceTensor = await PredictRaw(audioInput);
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

            return ((Emotion)maxIndex, maxValue);
        }

        public async Awaitable<(Emotion, float)> Predict(AudioClip clip)
        {
            var norm = PrepareAudio(clip);
            return await Predict(norm);
        }

        public static Tensor<float> PrepareAudio(AudioClip originalClip)
        {
            int targetLengthSeconds = 30;

            int channels = originalClip.channels;
            int frequency = originalClip.frequency;

            int targetSamples = targetLengthSeconds * frequency;

            float[] data = new float[targetSamples];
            originalClip.GetData(data, 0);

            return new Tensor<float>(new TensorShape(1, targetSamples), data);
        }

        void OnDisable()
        {
            logmelVoiceWorker?.Dispose();
            voiceWorker?.Dispose();
        }
    }
}
