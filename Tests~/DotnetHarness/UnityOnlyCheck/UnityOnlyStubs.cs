using Unity.InferenceEngine;

namespace UnityEditor
{
    public static class AssetDatabase
    {
        public static T LoadAssetAtPath<T>(string path) where T : class => null;
    }
}

namespace Unity.InferenceEngine
{
    public static class TensorReadbackExtensions
    {
        public static Tensor ReadbackAndClone(this Tensor tensor) => tensor;
    }
}
