using Unity.InferenceEngine;

static class Utils
{
    // Load the model asset and add softmax to the model's output
    static Model PrepareModel(ModelAsset asset)
    {
        var src = ModelLoader.Load(asset);

        var fg = new FunctionalGraph();
        var ins = fg.AddInputs(src);
        var outs = Functional.Forward(src, ins);
        var softmax = Functional.Softmax(outs[0]);

        fg.AddOutput(softmax);
        return graph.Compile();
    }
}
