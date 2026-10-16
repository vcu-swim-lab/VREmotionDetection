using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace Unity.Collections
{
    public enum Allocator { Temp }

    public struct NativeArray<T> where T : struct
    {
        private readonly T[] data;

        public NativeArray(int length, Allocator allocator) { data = new T[length]; }

        public int Length => data.Length;

        public T this[int index]
        {
            get => data[index];
            set => data[index] = value;
        }

        public void Dispose() { }
    }
}

namespace UnityEngine
{
    public class SerializeField : Attribute { }

    public class TooltipAttribute : Attribute
    {
        public TooltipAttribute(string text) { }
    }

    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
    public class RequireComponent : Attribute
    {
        public RequireComponent(Type type) { }
    }

    public class CreateAssetMenuAttribute : Attribute
    {
        public string fileName;
        public string menuName;
    }

    public class Object
    {
        public string name;

        public static void Destroy(Object o) { }

        public static void DestroyImmediate(Object o) { }
    }

    public class ScriptableObject : Object
    {
        public static T CreateInstance<T>() where T : ScriptableObject, new() => new T();
    }

    public class Component : Object
    {
        public GameObject gameObject = new GameObject();

        public T GetComponent<T>() where T : class => gameObject.GetComponent<T>();

        public static void print(object message) { }
    }

    public class Behaviour : Component { }

    public class MonoBehaviour : Behaviour
    {
        public System.Threading.CancellationToken destroyCancellationToken => default;
    }

    public class GameObject : Object
    {
        private readonly List<object> components = new List<object>();

        public GameObject() { }

        public GameObject(string name) { this.name = name; }

        public T GetComponent<T>() where T : class => components.OfType<T>().FirstOrDefault();

        public T AddComponent<T>() where T : Component, new()
        {
            var component = new T { gameObject = this };
            components.Add(component);
            return component;
        }
    }

    public static class Debug
    {
        public static readonly List<string> Warnings = new List<string>();
        public static readonly List<string> Errors = new List<string>();

        public static void Log(object message) { }

        public static void LogWarning(object message)
        {
            lock (Warnings) Warnings.Add(message.ToString());
        }

        public static void LogError(object message)
        {
            lock (Errors) Errors.Add(message.ToString());
        }

        public static void Assert(bool condition, string message) { }
    }

    public static class Time
    {
        private static readonly System.Diagnostics.Stopwatch Clock = System.Diagnostics.Stopwatch.StartNew();

        public static float? Override;

        public static float time => Override ?? realtimeSinceStartup;

        public static float realtimeSinceStartup => (float)Clock.Elapsed.TotalSeconds;
    }

    public static class Application
    {
        public static string persistentDataPath = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), "vr-emotion-harness-" + Guid.NewGuid().ToString("N"));
    }

    public static class Mathf
    {
        public static float Min(float a, float b) => Math.Min(a, b);

        public static float Max(float a, float b) => Math.Max(a, b);

        public static float Pow(float a, float b) => (float)Math.Pow(a, b);

        public static int CeilToInt(float a) => (int)Math.Ceiling(a);
    }

    public static class JsonUtility
    {
        public static string ToJson(object o) => JsonConvert.SerializeObject(o);

        public static T FromJson<T>(string json) => JsonConvert.DeserializeObject<T>(json);
    }

    public class AudioClip
    {
        public int channels = 1;
        public int frequency = 16000;
        public int samples = 480000;

        public bool GetData(float[] data, int offset) => true;
    }

    public static class Microphone
    {
        public static int StartCalls;

        public static AudioClip Start(string device, bool loop, int length, int frequency)
        {
            StartCalls++;
            return new AudioClip();
        }

        public static int GetPosition(string device) => 1;
    }

    public enum RuntimePlatform { WindowsEditor }
}

namespace UnityEngine.Events
{
    public class UnityEvent<T>
    {
        public void Invoke(T value) { }
    }
}

namespace UnityEngine.Networking
{
    public class UploadHandler { }

    public class UploadHandlerRaw : UploadHandler
    {
        public byte[] Data;

        public UploadHandlerRaw(byte[] data) { Data = data; }
    }

    public class DownloadHandler
    {
        public string text { get; set; } = "";
    }

    public class DownloadHandlerBuffer : DownloadHandler { }

    public class UnityWebRequestAsyncOperation
    {
        public TaskAwaiter GetAwaiter() => Task.CompletedTask.GetAwaiter();
    }

    public class UnityWebRequest : IDisposable
    {
        public const string kHttpVerbPOST = "POST";

        public UploadHandler uploadHandler;
        public DownloadHandler downloadHandler;
        public int timeout;

        public enum Result { InProgress, Success, ConnectionError, ProtocolError, DataProcessingError }

        public static bool ForceDataProcessingError;
        public static Dictionary<string, string> ResponseHeaders = new Dictionary<string, string>();

        public Result result { get; private set; }

        public static Func<string, string, Dictionary<string, string>, (long code, string text)> Handler;
        public static readonly List<string> Urls = new List<string>();

        private readonly string url;
        private readonly Dictionary<string, string> headers = new Dictionary<string, string>();

        public long responseCode { get; private set; }

        public string error { get; private set; }

        public UnityWebRequest(string url, string method) { this.url = url; }

        public void SetRequestHeader(string name, string value) { headers[name] = value; }

        public string GetResponseHeader(string name) => ResponseHeaders.TryGetValue(name, out var value) ? value : null;

        public UnityWebRequestAsyncOperation SendWebRequest()
        {
            string body = System.Text.Encoding.UTF8.GetString(((UploadHandlerRaw)uploadHandler).Data);
            lock (Urls) Urls.Add(url);
            var (code, text) = Handler(url, body, headers);
            responseCode = code;
            result = ForceDataProcessingError ? Result.DataProcessingError : code == 0 ? Result.ConnectionError : code >= 400 ? Result.ProtocolError : Result.Success;
            if (ForceDataProcessingError) error = "Failed to receive data";
            else if (code == 0) error = text;
            else downloadHandler.text = text;
            return new UnityWebRequestAsyncOperation();
        }

        public void Dispose() { }
    }
}

namespace UnityEngine
{
    [AsyncMethodBuilder(typeof(AwaitableBuilder))]
    public class Awaitable
    {
        internal readonly Task task;

        internal Awaitable(Task task) { this.task = task; }

        public TaskAwaiter GetAwaiter() => task.GetAwaiter();

        public static int FrameDelayMs = 1;

        public static Awaitable NextFrameAsync() => new Awaitable(Task.Delay(FrameDelayMs));

        public static Awaitable WaitForSecondsAsync(float seconds) => new Awaitable(Task.Delay((int)(seconds * 1000)));
    }

    [AsyncMethodBuilder(typeof(AwaitableBuilder<>))]
    public class Awaitable<T>
    {
        internal readonly Task<T> task;

        internal Awaitable(Task<T> task) { this.task = task; }

        public TaskAwaiter<T> GetAwaiter() => task.GetAwaiter();
    }

    public struct AwaitableBuilder
    {
        private AsyncTaskMethodBuilder inner;

        public static AwaitableBuilder Create() => new AwaitableBuilder { inner = AsyncTaskMethodBuilder.Create() };

        public void Start<TSM>(ref TSM sm) where TSM : IAsyncStateMachine => inner.Start(ref sm);

        public void SetStateMachine(IAsyncStateMachine sm) => inner.SetStateMachine(sm);

        public void SetResult() => inner.SetResult();

        public void SetException(Exception e) => inner.SetException(e);

        public void AwaitOnCompleted<TA, TSM>(ref TA a, ref TSM sm) where TA : INotifyCompletion where TSM : IAsyncStateMachine => inner.AwaitOnCompleted(ref a, ref sm);

        public void AwaitUnsafeOnCompleted<TA, TSM>(ref TA a, ref TSM sm) where TA : ICriticalNotifyCompletion where TSM : IAsyncStateMachine => inner.AwaitUnsafeOnCompleted(ref a, ref sm);

        public Awaitable Task => new Awaitable(inner.Task);
    }

    public struct AwaitableBuilder<T>
    {
        private AsyncTaskMethodBuilder<T> inner;

        public static AwaitableBuilder<T> Create() => new AwaitableBuilder<T> { inner = AsyncTaskMethodBuilder<T>.Create() };

        public void Start<TSM>(ref TSM sm) where TSM : IAsyncStateMachine => inner.Start(ref sm);

        public void SetStateMachine(IAsyncStateMachine sm) => inner.SetStateMachine(sm);

        public void SetResult(T result) => inner.SetResult(result);

        public void SetException(Exception e) => inner.SetException(e);

        public void AwaitOnCompleted<TA, TSM>(ref TA a, ref TSM sm) where TA : INotifyCompletion where TSM : IAsyncStateMachine => inner.AwaitOnCompleted(ref a, ref sm);

        public void AwaitUnsafeOnCompleted<TA, TSM>(ref TA a, ref TSM sm) where TA : ICriticalNotifyCompletion where TSM : IAsyncStateMachine => inner.AwaitUnsafeOnCompleted(ref a, ref sm);

        public Awaitable<T> Task => new Awaitable<T>(inner.Task);
    }
}

namespace Unity.InferenceEngine
{
    public enum BackendType { GPUCompute, CPU }

    public class ModelAsset : UnityEngine.ScriptableObject { }

    public class Model { }

    public struct TensorShape
    {
        private readonly int[] dims;

        public TensorShape(params int[] dims) { this.dims = dims; }

        public int this[int axis] => dims[axis];

        public int rank => dims.Length;

        public int length => dims.Aggregate(1, (a, b) => a * b);

        public override bool Equals(object obj) => obj is TensorShape other && dims.SequenceEqual(other.dims);

        public override int GetHashCode() => dims.Length;
    }

    public abstract class Tensor : IDisposable
    {
        public void Dispose() { }

        public abstract UnityEngine.Awaitable<Tensor> ReadbackAndCloneAsync();
    }

    public class Tensor<T> : Tensor
    {
        private readonly T[] data;

        public TensorShape shape { get; }

        public Tensor(TensorShape shape, T[] data)
        {
            this.shape = shape;
            this.data = data;
        }

        public T[] DownloadToArray() => (T[])data.Clone();

        public override UnityEngine.Awaitable<Tensor> ReadbackAndCloneAsync() =>
            new UnityEngine.Awaitable<Tensor>(Delayed(this));

        private static async Task<Tensor> Delayed(Tensor tensor)
        {
            if (Worker.ReadbackDelayMs > 0) await Task.Delay(Worker.ReadbackDelayMs);
            return tensor;
        }
    }

    public class Worker : IDisposable
    {
        public static float[] Canned = new float[7];
        public static int Runs;
        public static int ReadbackDelayMs;
        public static Func<float[]> Source;

        public Worker(Model model, BackendType backend) { }

        public void Schedule(Tensor input) { Runs++; }

        public Tensor PeekOutput() => new Tensor<float>(new TensorShape(1, 7), (Source?.Invoke() ?? Canned).ToArray());

        public void Dispose() { }
    }

    public static class TensorExtensions
    {
        public static T[] AsReadOnlyNativeArray<T>(this Tensor<T> tensor) => tensor.DownloadToArray();
    }

    public static class ModelLoader
    {
        public static Model Load(ModelAsset asset) => new Model();
    }

    public class FunctionalTensor { }

    public class FunctionalGraph
    {
        public FunctionalTensor[] AddInputs(Model model) => new FunctionalTensor[0];

        public void AddOutput(FunctionalTensor tensor) { }

        public Model Compile() => new Model();
    }

    public static class Functional
    {
        public static FunctionalTensor[] Forward(Model model, FunctionalTensor[] inputs) => new FunctionalTensor[1];

        public static FunctionalTensor Softmax(FunctionalTensor tensor) => tensor;
    }
}
