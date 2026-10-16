using System.Linq;
using Unity.InferenceEngine;

namespace VcuSwimLab.Backends.SparseVr
{
    public static class WindowTensor
    {
        public static Tensor<float> From(float[][] window)
        {
            int frameSize = window[0].Length;
            float[] data = window.SelectMany(frame => frame).ToArray();
            return new Tensor<float>(new TensorShape(1, window.Length, frameSize), data);
        }
    }
}
