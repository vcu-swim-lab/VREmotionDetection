using System;
using System.Diagnostics;
using Unity.InferenceEngine;
using Unity.Collections;
using UnityEngine;
using System.Collections.Generic;

using Debug = UnityEngine.Debug;

// One face prediction together with everything needed to reproduce it offline.
//
// `Window` is an independent deep copy of the exact frames that were converted into the
// input tensor, so a consumer can log it and later replay it through the same ONNX graph
// outside Unity. Holding a reference into the live ring buffer would not work: its arrays
// are recycled on every poll, so the contents would have moved on by the time they were
// written out.
public readonly struct FacePrediction
{
    // Highest scoring class.
    public readonly Emotion Emotion;

    // Score of `Emotion`. A probability, provided the model was prepared without an
    // extra softmax (see `Utils.PrepareModel`).
    public readonly float Confidence;

    // All model outputs, in `Emotion` enum order:
    // Anger, Disgust, Fear, Happiness, Neutral, Sadness, Surprise.
    public readonly float[] Scores;

    // The input window, oldest frame first: [frames][features].
    public readonly float[][] Window;

    // Ring buffer generation after the window was captured. The difference between two
    // consecutive predictions is the number of valid frames that arrived in between, so
    // a smaller-than-expected step means face tracking dropped out.
    public readonly long Generation;

    internal FacePrediction(Emotion emotion, float confidence, float[] scores, float[][] window, long generation)
    {
        Emotion = emotion;
        Confidence = confidence;
        Scores = scores;
        Window = window;
        Generation = generation;
    }
}

[RequireComponent(typeof(OVRFaceExpressions))]
[RequireComponent(typeof(DeviceManager))]
public class NewFaceAuModel : MonoBehaviour
{
    public const int WindowFrames = 5;
    public const int FeaturesPerFrame = 70;

    // How long to wait for a full window of *valid* frames before giving up. At 10 Hz a
    // window needs 0.5 s, so this allows for a few seconds of lost tracking before the
    // caller is told the prediction could not be made.
    public const float DefaultTimeoutSeconds = 3f;

    [SerializeField]
    private ModelAsset faceModel;

    private Model faceModelObject;
    private Worker faceWorker;

    private RingBuffer<float[]> inputBuffer;

    // Name of the bound model asset, for session metadata.
    public string FaceModelName => faceModel != null ? faceModel.name : null;

    public void Start()
    {
        Debug.Assert(faceModel != null, $"Face model not assigned in ${GetType().Name}");

        // The face graphs already end in a Softmax node; appending another would leave
        // `Confidence` as something that is not a probability.
        faceModelObject = Utils.PrepareModel(faceModel, appendSoftmax: false);
        faceWorker = new Worker(faceModelObject, BackendType.GPUCompute);

        var deviceManager = GetComponent<DeviceManager>();

        var auDevice = deviceManager.Require(InputType.FaceAU);
        if (auDevice == null)
        {
            Debug.LogError("Could not create action units device!");
        }

        inputBuffer = new(WindowFrames, () => new float[FeaturesPerFrame]);
        inputBuffer.Listen(auDevice);

        Debug.Log("NewFaceAuModel initialized.");
    }

    async Awaitable<Tensor<float>> Infer(Tensor<float> input)
    {
        faceWorker.Schedule(input);
        return await faceWorker.PeekOutput().ReadbackAndCloneAsync() as Tensor<float>;
    }

    // Wait until the ring buffer holds a full window of valid frames, then take an
    // independent copy of it. Returns null if the window could not be filled in time,
    // which in practice means face tracking was lost or never started.
    private async Awaitable<float[][]> AwaitWindow(float timeoutSeconds)
    {
        inputBuffer.Clear();

        float deadline = Time.realtimeSinceStartup + timeoutSeconds;

        while (!inputBuffer.Full)
        {
            if (Time.realtimeSinceStartup >= deadline)
            {
                Debug.LogWarning(
                    $"{nameof(NewFaceAuModel)}: timed out after {timeoutSeconds:F1}s waiting for " +
                    $"{WindowFrames} valid face frames. Face tracking is probably unavailable.");
                return null;
            }

            await Awaitable.NextFrameAsync();
        }

        return inputBuffer.Snapshot();
    }

    // TODO: `Predict` that doesn't wait on new data, just uses whatever is in the buffer.

    public async Awaitable<Tensor<float>> PredictRaw()
    {
        var window = await AwaitWindow(DefaultTimeoutSeconds);
        if (window == null)
        {
            return null;
        }

        using var input = window.ToTensor();
        return await Infer(input);
    }

    // Full prediction result, including the exact input window. Returns null when a
    // window of valid frames could not be collected before the timeout.
    public async Awaitable<FacePrediction?> PredictDetailed(float timeoutSeconds = DefaultTimeoutSeconds)
    {
        var window = await AwaitWindow(timeoutSeconds);
        if (window == null)
        {
            return null;
        }

        long generation = inputBuffer.Generation;

        using var input = window.ToTensor();
        using var output = await Infer(input);

        var scores = output.DownloadToArray();

        int maxIndex = 0;
        for (int i = 1; i < scores.Length; ++i)
        {
            if (scores[i] > scores[maxIndex])
            {
                maxIndex = i;
            }
        }

        return new FacePrediction((Emotion)maxIndex, scores[maxIndex], scores, window, generation);
    }

    public async Awaitable<(Emotion, float)> Predict()
    {
        var prediction = await PredictDetailed();
        if (prediction == null)
        {
            // Preserve the existing signature: callers that cannot express "no reading"
            // fall back to Neutral with zero confidence.
            return (Emotion.Neutral, 0f);
        }

        return (prediction.Value.Emotion, prediction.Value.Confidence);
    }


    void OnDisable()
    {
        faceWorker.Dispose();
    }
}
