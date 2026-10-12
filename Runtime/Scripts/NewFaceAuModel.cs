using System;
using System.Diagnostics;
using Unity.InferenceEngine;
using Unity.Collections;
using UnityEngine;
using System.Collections.Generic;
using VcuSwimLab.Calibration;

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

    // Uses movement calibration when it is ready (see `CalibrateEmotion`), and the model's own
    // answer otherwise. The returned confidence is always the model's probability for the
    // returned emotion.
    public async Awaitable<(Emotion, float)> Predict()
    {
        var prediction = await PredictDetailed();
        if (prediction == null)
        {
            // Preserve the existing signature: callers that cannot express "no reading"
            // fall back to Neutral with zero confidence.
            return (Emotion.Neutral, 0f);
        }

        var emotion = CalibratedEmotion(prediction.Value) ?? prediction.Value.Emotion;
        return (emotion, prediction.Value.Scores[(int)emotion]);
    }

    // ---- Movement calibration (optional) -------------------------------------------------
    //
    // Learns how this player makes each emotion, then `Predict` uses it. Without it, nothing
    // changes. Typical use:
    //
    //     predictor.StartCalibration();
    //     foreach (Emotion e in Enum.GetValues(typeof(Emotion)))
    //     {
    //         // show "make a <e> face" first
    //         await predictor.CalibrateEmotion(e);
    //     }
    //     if (!predictor.FinishCalibration()) Debug.LogWarning(predictor.CalibrationError);

    private MovementCalibrator calibrator;

    // True when `Predict` is using movement calibration.
    public bool IsCalibrated => calibrator != null && calibrator.IsReady;

    // Why calibration failed; empty when it did not, null when it never started.
    public string CalibrationError => calibrator?.Error;

    // Start a new calibration, forgetting any previous one.
    public void StartCalibration()
    {
        calibrator = new MovementCalibrator(Enum.GetNames(typeof(Emotion)));
    }

    // Stop using calibration; `Predict` goes back to the model's own answer.
    public void ClearCalibration()
    {
        calibrator = null;
    }

    // Collect one emotion while the player makes that face. Takes one sample per
    // `secondsBetweenSamples`; the first is dropped while the face is still changing, so
    // with the default it lasts about ten seconds. Samples without face tracking are retried.
    // Returns false when `maxAttempts` samples did not give a complete emotion.
    public async Awaitable<bool> CalibrateEmotion(Emotion emotion, float secondsBetweenSamples = 1f,
                                                  int maxAttempts = 20)
    {
        if (calibrator == null)
        {
            throw new InvalidOperationException("call StartCalibration before CalibrateEmotion");
        }

        calibrator.BeginEmotion((int)emotion);

        for (int attempt = 0; !calibrator.IsEmotionComplete((int)emotion); ++attempt)
        {
            if (attempt >= maxAttempts)
            {
                return false;
            }

            float nextSample = Time.time + secondsBetweenSamples;

            var prediction = await PredictDetailed();
            if (prediction.HasValue)
            {
                calibrator.Observe(prediction.Value.Scores, prediction.Value.Window);
            }

            float remaining = nextSample - Time.time;
            if (remaining > 0)
            {
                await Awaitable.WaitForSecondsAsync(remaining);
            }
        }

        return true;
    }

    // Build the calibration once every emotion is collected. Returns false, with
    // `CalibrationError` set, if it could not be built; `Predict` then stays uncalibrated.
    public bool FinishCalibration()
    {
        return calibrator != null && calibrator.Finish();
    }

    // The calibrated emotion for a prediction from `PredictDetailed`, or null when
    // calibration is not ready.
    public Emotion? CalibratedEmotion(FacePrediction prediction)
    {
        var decision = calibrator?.Classify(prediction.Scores, prediction.Window);
        return decision.HasValue ? (Emotion)decision.Value.ClassIndex : (Emotion?)null;
    }


    void OnDisable()
    {
        faceWorker.Dispose();
    }
}
