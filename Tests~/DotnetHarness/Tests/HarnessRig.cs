using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Unity.InferenceEngine;
using UnityEngine;
#if !LEGACY_GLOBAL_NAMESPACE
using VcuSwimLab.VREmotionDetection;
#endif

internal sealed class HarnessRig : IDisposable
{
    private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

    private readonly CancellationTokenSource stop = new CancellationTokenSource();
    private readonly Task feeder;
    private volatile float[] currentFace = Face(0);
    private volatile bool valid = true;

    public HarnessRig(bool useCalibration = true, bool withModel = true, bool start = true, string modelName = "harness-model",
                      string storageFolder = null)
    {
        Application.persistentDataPath = storageFolder ?? Path.Combine(Path.GetTempPath(), "rig-" + Guid.NewGuid().ToString("N"));
        Time.Override = 0f;
        Worker.Canned = Scores(0);
        Worker.Source = null;
        Worker.ReadbackDelayMs = 0;
        lock (Debug.Warnings) Debug.Warnings.Clear();
        lock (Debug.Errors) Debug.Errors.Clear();
        lock (Debug.Logs) Debug.Logs.Clear();
        Microphone.StartCalls = 0;

        Host = new GameObject("rig");
        Ovr = Host.AddComponent<OVRFaceExpressions>();
        Devices = Host.AddComponent<DeviceManager>();
        Model = Host.AddComponent<NewFaceAuModel>();

        if (withModel)
        {
            Field(Model, "faceModel", new ModelAsset { name = modelName });
        }

        Field(Model, "useCalibration", useCalibration);

        float clock = 0f;
        feeder = Task.Run(async () =>
        {
            while (!stop.IsCancellationRequested)
            {
                clock += 0.1f;
                Time.Override = clock;
                Ovr.ValidExpressions = valid;
                Ovr.Values = (float[])currentFace.Clone();
                Call(Devices, "Update");
                await Task.Delay(1);
            }
        });

        if (start)
        {
            Model.Start();
        }
    }

    public GameObject Host { get; }

    public OVRFaceExpressions Ovr { get; }

    public DeviceManager Devices { get; }

    public NewFaceAuModel Model { get; }

    public static float[] Face(int cls)
    {
        var frame = new float[70];
        for (int a = 0; a < 63; ++a) frame[a] = 0.02f;
        for (int a = cls * 9; a < cls * 9 + 9; ++a) frame[a] = 0.8f;
        return frame;
    }

    public static float[] Scores(int peak)
    {
        var scores = Enumerable.Repeat(0.04f, 7).ToArray();
        scores[peak] = 0.76f;
        return scores;
    }

    public static void Field(object target, string name, object value) =>
        target.GetType().GetField(name, Instance).SetValue(target, value);

    public static object Call(object target, string name) =>
        target.GetType().GetMethod(name, Instance)?.Invoke(target, null);

    public void Show(int cls, bool tracked = true)
    {
        currentFace = Face(cls);
        valid = tracked;
    }

    public async Task Settle() => await Task.Delay(40);

    public void Dispose()
    {
        stop.Cancel();
        try { feeder.Wait(1000); } catch (AggregateException) { }
        Time.Override = null;
    }
}
