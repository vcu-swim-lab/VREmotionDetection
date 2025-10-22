using Unity.InferenceEngine;
using UnityEngine;

[RequireComponent(typeof(OVRFaceExpressions))]
public class FaceAuModel : MonoBehaviour
{
    [SerializeField]
    private ModelAsset modelAsset;

    private Model model;
    private Worker worker;

    private RingBuffer<float[]> inputBuffer;

    public void Start()
    {
        model = ModelLoader.Load(modelAsset);
        worker = new Worker(model, BackendType.CPU);

        if (!TryGetComponent<DeviceManager>(out var deviceManager))
        {
            Debug.LogError("FaceAuModel requires a DeviceManager to be attached to the same GameObject.");
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

    async Awaitable<Tensor<float>> Infer(Tensor<float> input)
    {
        worker.Schedule(input);
        var tensor = await worker.PeekOutput().ReadbackAndCloneAsync();
        return tensor as Tensor<float>;
    }

    // TODO: `Predict` that doesn't wait on new data, just uses whatever is in the buffer.

    public async Awaitable<(Emotion, float)> Predict()
    {
        inputBuffer.Clear();

        while (!inputBuffer.Full)
        {
            await Awaitable.NextFrameAsync();
        }

        // TODO: should you dispose the tensor?
        var input = inputBuffer.ToTensor();

        using var output = await Infer(input);
        var outputArray = output.AsReadOnlyNativeArray();

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

        return ((Emotion)maxIndex, maxValue);
    }


    public void Dispose()
    {
        worker.Dispose();
    }
}