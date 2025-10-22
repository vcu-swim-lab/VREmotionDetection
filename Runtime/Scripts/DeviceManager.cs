
using System;
using System.Collections.Generic;
using System.Linq;
using Unity.InferenceEngine;
using UnityEngine;

#if UNITY_ANDROID && !UNITY_EDITOR
using UnityEngine.Android;
#endif


// TODO:
// - RingBuffer.Listen(device)
// - RingBuffer.RawData() => T[]
// - Utils.ToTensor(RingBuffer<float[]>)

public class RingBuffer<T>
{
    internal readonly T[] buffer;
    internal int counter = 0;

    internal RingBuffer(int size, Func<T> defaultVal)
    {
        buffer = new T[size];
        for (int i = 0; i < size; ++i)
        {
            buffer[i] = defaultVal();
        }
    }

    public bool Full => counter >= buffer.Length;

    public void Clear()
    {
        counter = 0;
    }
}

public static class RingBufferExtensions
{
    static public Tensor<float> ToTensor(this RingBuffer<float[]> ring)
    {
        int frameSize = ring.buffer[0].Length;
        float[] data = ring.buffer.SelectMany(x => x).ToArray();
        return new Tensor<float>(new TensorShape(1, ring.buffer.Length, frameSize), data);
    }

    static public void Listen(this RingBuffer<float[]> ring, Device device)
    {
        device.OnPoll += (type, data) =>
        {
            // shift left to make room for new data
            for (int i = 0; i < ring.buffer.Length - 1; ++i)
            {
                ring.buffer[i] = ring.buffer[i + 1];
            }

            var tmp = ring.buffer[ring.buffer.Length - 1];
            Array.Copy(data, tmp, data.Length);
            ring.counter++;
        };
    }
}

public enum InputType
{
    FaceAU,
    Sound,
    Count, // meta, just the number of input types
}


public enum Emotion
{
    Anger,
    Disgust,
    Fear,
    Happiness,
    Neutral,
    Sadness,
    Surprise,
}


#region devices
public abstract class Device
{
    internal Device(GameObject parent) { }

    // ty - the type of input data given by the device
    // width - the number of entries given per reading (eg. facial data has 70 readings, one per action unit)
    internal abstract (InputType ty, int width) InputSpec { get; }
    internal abstract float Poll(float[] data);

    internal delegate void PollDelegate(InputType type, float[] data);
    internal PollDelegate OnPoll;
}

public class AUDevice : Device
{
    public AUDevice(GameObject go)
        : this(go, 10) { }

    internal AUDevice(GameObject go, int freq)
        : base(go)
    {
        faceExpressions = go.GetComponent<OVRFaceExpressions>();
        pollInterval = 1.0f / freq;
    }

    internal override (InputType ty, int width) InputSpec => (InputType.FaceAU, 70);

    internal override float Poll(float[] data)
    {
        if (faceExpressions.ValidExpressions)
        {
            faceExpressions.CopyTo(data);
        }

        return pollInterval;
    }

    private readonly OVRFaceExpressions faceExpressions;
    private readonly float pollInterval;
}

// TODO: fully clarify the semantics of polling a sound device (eg. Whisper needs 480000 samples, ie. 30 seconds recorded at 16KHz frequency).
// ^ this is different from say AU readings, where eaching reading has 70 features.
public class SoundDevice : Device
{
    private AudioClip sound;

    public SoundDevice(GameObject go)
        : base(go)
    {
        StartRecordingAsync();
    }

    // TODO: recheck
    internal override (InputType ty, int width) InputSpec => (InputType.Sound, 16000);

    internal override float Poll(float[] data)
    {
        // TODO: recheck this
        int pos = Microphone.GetPosition(null);
        sound.GetData(data, pos - data.Length);
        return 1f; // poll every second
    }

    async void StartRecordingAsync()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (!Permission.HasUserAuthorizedPermission(Permission.Microphone))
            {
                Permission.RequestUserPermission(Permission.Microphone);
            }
            else
            {
                Debug.Log("Already have mic permission");
            }

                Debug.Log("Checked mic permissions");
#endif

        sound = Microphone.Start(null, true, 30, 16000);

        while (!(Microphone.GetPosition(null) > 0)) // Wait until the recording has started
        {
            await Awaitable.NextFrameAsync();
        }
    }
}
#endregion


public class DeviceManager : MonoBehaviour
{
    internal Dictionary<InputType, Type> allDevicesInProgram;

    internal Dictionary<InputType, Device> devices = new();
    internal bool[] enabledInput = new bool[(int)InputType.Count];

    private readonly Dictionary<InputType, float> nextPollTime = new();

    // This should run before any model that activates a device, hence `Awake` not `Start`
    void Awake()
    {
        Array.Fill(enabledInput, false);

        allDevicesInProgram = DevicesByType();
        Debug.Assert(allDevicesInProgram != null, "Internal error when setting up DeviceManager");

        string[] deviceNames = allDevicesInProgram.Keys.Select(k => $"({k} => {allDevicesInProgram[k]})").ToArray();
        Debug.Log($"DeviceManager: all devices in program: {string.Join(",", deviceNames)}");
    }

    void Update()
    {
        //if (devices.Keys.Count == 0)
        //{
        //    Debug.LogWarning("No devices attached to DeviceManager; nothing to poll.");
        //    return;
        //}

        float now = Time.time;
        foreach (var (ty, device) in devices)
        {
            if (now >= nextPollTime[ty])
            {
                var size = device.InputSpec.width;
                var buf = new float[size];
                float interval = device.Poll(buf);
                nextPollTime[ty] = now + interval;
                device.OnPoll(device.InputSpec.ty, buf);
            }
        }
    }

    public Device Require(InputType ty)
    {
        print($"Checking device of input type {ty}");
        if (enabledInput[(int)ty]) return devices[ty];

        print("Device is not enabled yet");

        Debug.Assert(allDevicesInProgram != null, "Internal error when setting up DeviceManager");
        Debug.Assert(allDevicesInProgram.ContainsKey(ty), $"No device of type {ty} is available in the program.");
        print($"Device type is available, device name: {allDevicesInProgram[ty]}");

        Debug.Assert(gameObject != null, "DeviceManager must be attached to a GameObject.");

        var device = Construct(allDevicesInProgram[ty], gameObject);

        devices[ty] = device;
        nextPollTime[ty] = Time.time;
        enabledInput[(int)ty] = true;

        print("Marked device as enabled");

        return device;
    }

    internal void Disable(InputType ty)
    {
        if (devices.ContainsKey(ty))
        {
            devices.Remove(ty);
        }
    }

    private Dictionary<InputType, Type> DevicesByType()
    {
        var baseType = typeof(Device);
        return AppDomain.CurrentDomain.GetAssemblies()
            .SelectMany(a => a.GetTypes().Where(t => !t.IsAbstract && baseType.IsAssignableFrom(t)))
            .ToDictionary((type) => Construct(type, gameObject).InputSpec.ty);
    }

    private static Device Construct(Type type, GameObject go)
    {
        print($"Creating device {type.Name}");
        var ctor = type.GetConstructor(new Type[] { typeof(GameObject) });
        if (ctor == null)
        {
            throw new Exception($"Device subclass {type} must have a constructor that takes a GameObject as its only argument.");
        }
        var args = new object[] { go };
        return (Device)ctor.Invoke(args);
    }
}
