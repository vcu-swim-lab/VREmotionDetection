using Unity.InferenceEngine;

namespace Unity.InferenceEngine
{
    public static class TensorReadbackExtensions
    {
        public static Tensor ReadbackAndClone(this Tensor tensor) => tensor;
    }
}
