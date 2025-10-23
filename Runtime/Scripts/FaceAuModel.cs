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
        Debug.Assert(naturalModel != null, $"Natural model not assigned in ${nameof(this)}");
        Debug.Assert(actedModel != null, $"Acted model not assigned in ${nameof(this)}");

        naturalModelObject = ModelLoader.Load(naturalModel);
        naturalWorker = new Worker(naturalModelObject, BackendType.CPU);

        actedModelObject = ModelLoader.Load(actedModel);
        actedWorker = new Worker(actedModelObject, BackendType.CPU);

        if (!TryGetComponent<DeviceManager>(out var deviceManager))
        {
            Debug.LogError($"{nameof(this)} requires a DeviceManager to be attached to the same GameObject.");
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

    public async Awaitable<(Emotion, float)> Predict(float naturalWeight = 1f, float actedWeight = 1f)
    {
        inputBuffer.Clear();

        while (!inputBuffer.Full)
        {
            await Awaitable.NextFrameAsync();
        }

        // TODO: should you dispose the tensor?
        var input = inputBuffer.ToTensor();

        var (nat, act) = await Infer(input);
        using var natArr = nat.AsReadOnlyNativeArray();
        using var actArr = nat.AsReadOnlyNativeArray();

        using var outputArray = new NativeArray<float>(natArr.Length, Allocator.Temp);
        for (int i = 0; i < natArr.Length; ++i)
            outputArray[i] = natArr[i] * naturalWeight + actArr[i] * actedWeight;

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