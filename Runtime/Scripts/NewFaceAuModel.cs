using System.Diagnostics;
using Unity.InferenceEngine;
using Unity.Collections;
using UnityEngine;
using System.Collections.Generic;

using Debug = UnityEngine.Debug;

[RequireComponent(typeof(OVRFaceExpressions))]
[RequireComponent(typeof(DeviceManager))]
public class NewFaceAuModel : MonoBehaviour
{
    [SerializeField]
    private ModelAsset faceModel;

    private Model faceModelObject;
    private Worker faceWorker;

    private RingBuffer<float[]> inputBuffer;

    public void Start()
    {
        Debug.Assert(faceModel != null, $"Face model not assigned in ${GetType().Name}");

        faceModelObject = ModelLoader.Load(faceModel);
        faceWorker = new Worker(faceModelObject, BackendType.CPU);

        var deviceManager = GetComponent<DeviceManager>();

        var auDevice = deviceManager.Require(InputType.FaceAU);
        if (auDevice == null)
        {
            Debug.LogError("Could not create action units device!");
        }

        inputBuffer = new(5, () => new float[70]);
        inputBuffer.Listen(auDevice);

        Debug.Log("NewFaceAuModel initialized.");
    }

    async Awaitable<Tensor<float>> Infer(Tensor<float> input)
    {
        faceWorker.Schedule(input);
        return await faceWorker.PeekOutput().ReadbackAndCloneAsync() as Tensor<float>;
    }

    // TODO: `Predict` that doesn't wait on new data, just uses whatever is in the buffer.

    public async Awaitable<Tensor<float>> PredictRaw()
    {
        inputBuffer.Clear();

        while (!inputBuffer.Full)
        {
            await Awaitable.NextFrameAsync();
        }

        // TODO: should you dispose the tensor?
        var input = inputBuffer.ToTensor();

        return await Infer(input);
    }

    public async Awaitable<(Emotion, float)> Predict()
    {
        using var face = await PredictRaw();
        var faceArr = face.AsReadOnlyNativeArray();

        int maxIndex = 0;
        float maxValue = faceArr[0];
        for (int i = 1; i < faceArr.Length; ++i)
        {
            if (faceArr[i] > maxValue)
            {
                maxValue = faceArr[i];
                maxIndex = i;
            }
        }

        face.Dispose();

        return ((Emotion)maxIndex, maxValue);
    }


    public void Dispose()
    {
        faceWorker.Dispose();
    }
}