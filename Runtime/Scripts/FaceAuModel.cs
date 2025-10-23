using System.Diagnostics;
using Unity.InferenceEngine;
using Unity.Collections;
using UnityEngine;

using Debug = UnityEngine.Debug;

[RequireComponent(typeof(OVRFaceExpressions))]
public class FaceAuModel : MonoBehaviour
{
    [SerializeField]
    private ModelAsset naturalModel;
    [SerializeField]
    private ModelAsset actedModel;

    private Model naturalModelObject;
    private Model actedModelObject;

    private Worker naturalWorker;
    private Worker actedWorker;

    private RingBuffer<float[]> inputBuffer;

    public void Start()
    {
        Debug.Assert(naturalModel != null, $"Natural model not assigned in ${GetType().Name}");
        Debug.Assert(actedModel != null, $"Acted model not assigned in ${GetType().Name}");

        naturalModelObject = ModelLoader.Load(naturalModel);
        naturalWorker = new Worker(naturalModelObject, BackendType.CPU);

        actedModelObject = ModelLoader.Load(actedModel);
        actedWorker = new Worker(actedModelObject, BackendType.CPU);

        if (!TryGetComponent<DeviceManager>(out var deviceManager))
        {
            Debug.LogError($"{GetType().Name} requires a DeviceManager to be attached to the same GameObject.");
            return;
        }

        var auDevice = deviceManager.Require(InputType.FaceAU);
        if (auDevice == null)
        {
            Debug.LogError("Could not create action units device!");
        }

        inputBuffer = new(5, () => new float[70]);
        inputBuffer.Listen(auDevice);

        Debug.Log("FaceAuModel initialized.");
    }

    async Awaitable<(Tensor<float>, Tensor<float>)> Infer(Tensor<float> input)
    {
        naturalWorker.Schedule(input);
        actedWorker.Schedule(input);

        var naturalTensor = await naturalWorker.PeekOutput().ReadbackAndCloneAsync();
        var actedTensor = await actedWorker.PeekOutput().ReadbackAndCloneAsync();
        return (
            naturalTensor as Tensor<float>,
            actedTensor as Tensor<float>//
        );
    }

    // TODO: `Predict` that doesn't wait on new data, just uses whatever is in the buffer.

    public async Awaitable<(Emotion, float)> PredictRaw()
    {
        inputBuffer.Clear();

        while (!inputBuffer.Full)
        {
            await Awaitable.NextFrameAsync();
        }

        // TODO: should you dispose the tensor?
        var input = inputBuffer.ToTensor();

        return Infer(input);
    }

    private static readonly Dictionary<Emotion, float> naturalWeightMap = new()
    {
        { Emotion.Neutral, 0.5f },
        { Emotion.Happiness, 0.3f },
        { Emotion.Sadness, 0.7f },
        { Emotion.Anger, 0.5f },
        { Emotion.Fear, 0.5f },
        { Emotion.Disgust, 1f },
        { Emotion.Surprise, 0.3f },
    };
    public async Awaitable<(Emotion, float)> Predict()
    {
        var (nat, act) = await PredictRaw();
        var natArr = nat.AsReadOnlyNativeArray();
        var actArr = act.AsReadOnlyNativeArray();

        var outputArray = new NativeArray<float>(natArr.Length, Allocator.Temp);
        for (int i = 0; i < natArr.Length; ++i)
            outputArray[i] = natArr[i] * naturalWeightMap[(Emotion)i] + actArr[i] * (1f - naturalWeightMap[(Emotion)i]);

        int maxIndex = 0;
        float maxValue = outputArray[0];
        for (int i = 1; i < outputArray.Length; ++i)
        {
            if (outputArray[i] > maxValue)
            {
                maxValue = outputArray[i];
                maxIndex = i;
            }
        }

        nat.Dispose();
        act.Dispose();

        return ((Emotion)maxIndex, maxValue);
    }


    public void Dispose()
    {
        naturalWorker.Dispose();
        actedWorker.Dispose();
    }
}