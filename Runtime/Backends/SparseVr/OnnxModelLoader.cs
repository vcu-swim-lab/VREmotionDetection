using System;
using Unity.InferenceEngine;

namespace VcuSwimLab.Backends.SparseVr
{
    public static class OnnxModelLoader
    {
        public static Model Prepare(ModelAsset asset, bool appendSoftmax)
        {
            if (asset == null)
            {
                throw new ArgumentNullException(nameof(asset));
            }

            var source = ModelLoader.Load(asset);

            if (!appendSoftmax)
            {
                return source;
            }

            var graph = new FunctionalGraph();
            var inputs = graph.AddInputs(source);
            var outputs = Functional.Forward(source, inputs);
            var softmax = Functional.Softmax(outputs[0]);

            graph.AddOutput(softmax);
            return graph.Compile();
        }
    }
}
