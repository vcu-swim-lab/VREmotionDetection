
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

    // Monotonic count of every frame ever pushed. Unlike `counter` this is never reset
    // by `Clear()`, so a consumer can tell how many frames actually arrived between two
    // predictions. A gap smaller than expected means frames were dropped upstream --
    // which, after the validity gating in `DeviceManager.Update`, means face tracking
    // was lost. This is the signal to use when diagnosing tracking dropouts offline.
    internal long generation = 0;

    public RingBuffer(int size, Func<T> defaultVal)
    {
        buffer = new T[size];
        for (int i = 0; i < size; ++i)
        {
            buffer[i] = defaultVal();
        }
    }

    public bool Full => counter >= buffer.Length;

    public long Generation => generation;

    public int Capacity => buffer.Length;

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

    // Same layout, but from a detached window (see `Snapshot`). Building the tensor from
    // the snapshot rather than from the live ring is what guarantees that a logged window
    // is byte-for-byte the input the model actually saw.
    static public Tensor<float> ToTensor(this float[][] window)
    {
        int frameSize = window[0].Length;
        float[] data = window.SelectMany(x => x).ToArray();
        return new Tensor<float>(new TensorShape(1, window.Length, frameSize), data);
    }

    static public void Push(this RingBuffer<float[]> ring, float[] data)
    {
        if (data == null)
        {
            throw new ArgumentNullException(nameof(data));
        }

        var tmp = ring.buffer[0];
        if (data.Length != tmp.Length)
        {
            throw new ArgumentException(
                $"Expected frame length {tmp.Length}, received {data.Length}.",
                nameof(data));
        }

        // Rotate the backing arrays instead of copying their references over one
        // another. This keeps every timestep backed by a distinct array.
        for (int i = 0; i < ring.buffer.Length - 1; ++i)
        {
            ring.buffer[i] = ring.buffer[i + 1];
        }
        ring.buffer[ring.buffer.Length - 1] = tmp;

        Array.Copy(data, tmp, data.Length);
        ring.counter++;
        ring.generation++;
    }

    // Independent deep copy of the window, oldest frame first. Callers that need to
    // record exactly what was fed to a model must use this: the ring's own arrays are
    // recycled on every push, so a reference held across a single poll is already stale.
    static public float[][] Snapshot(this RingBuffer<float[]> ring)
    {
        var copy = new float[ring.buffer.Length][];
        for (int i = 0; i < ring.buffer.Length; ++i)
        {
            copy[i] = (float[])ring.buffer[i].Clone();
        }
        return copy;
    }

    static public void Listen(this RingBuffer<float[]> ring, Device device)
    {
        device.OnPoll += (type, data) => ring.Push(data);
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

    // Fill `data` with one reading.
    //
    // interval - seconds until this device should be polled again.
    // valid    - whether `data` holds a genuine reading. When false the caller must
    //            discard the frame entirely rather than treating it as data; see
    //            `DeviceManager.PollAndDispatch`.
    internal abstract (float interval, bool valid) Poll(float[] data);

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

    internal override (float interval, bool valid) Poll(float[] data)
    {
        // When tracking is lost `data` is left untouched, ie. all zeros. A real face
        // never produces 70 exact zeros, so previously a dropout was indistinguishable
        // from a genuine neutral expression once it reached the model. Reporting it as
        // invalid keeps it out of the ring buffer entirely.
        if (faceExpressions == null || !faceExpressions.ValidExpressions)
        {
            return (pollInterval, false);
        }

        faceExpressions.CopyTo(data);
        return (pollInterval, true);
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

    internal override (float interval, bool valid) Poll(float[] data)
    {
        // `StartRecordingAsync` runs asynchronously, so the clip may not exist yet and
        // the ring buffer may not yet hold a full window of recorded audio.
        int pos = Microphone.GetPosition(null);
        if (sound == null || pos < data.Length)
        {
            return (1f, false);
        }

        // TODO: recheck this
        sound.GetData(data, pos - data.Length);
        return (1f, true); // poll every second
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
    // TODO: v-- this is only used by the inspector
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
                nextPollTime[ty] = now + PollAndDispatch(device);
            }
        }
    }

    // Poll a device once and forward the reading to its listeners only when the device
    // reports it as valid. An invalid reading is dropped, so downstream ring buffers do
    // not advance and never contain a fabricated frame.
    //
    // Extracted from `Update` so the gating can be exercised without a headset.
    // Returns the interval until the device should next be polled.
    internal static float PollAndDispatch(Device device)
    {
        var buf = new float[device.InputSpec.width];
        var (interval, valid) = device.Poll(buf);

        if (valid)
        {
            device.OnPoll?.Invoke(device.InputSpec.ty, buf);
        }

        return interval;
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
        // Only devices declared alongside `Device` itself are discovered. This scan
        // constructs every candidate to read its `InputSpec`, so widening it to all
        // loaded assemblies means arbitrary types get instantiated during `Awake` --
        // and two devices reporting the same `InputType` would throw on the duplicate
        // key. Restricting it keeps editor-only and consumer-defined subclasses (test
        // fakes in particular) from breaking play mode.
        var baseType = typeof(Device);
        return baseType.Assembly.GetTypes()
            .Where(t => !t.IsAbstract && baseType.IsAssignableFrom(t))
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
