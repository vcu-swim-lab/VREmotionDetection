using Unity.InferenceEngine;

internal static class Utils
{
    // Load a model asset, optionally appending a softmax to its first output.
    //
    // `appendSoftmax` is deliberately required: whether a graph needs it depends on
    // the exported model, and getting it wrong is silent. Verified against the assets
    // in `Runtime/Models`:
    //
    //   sparseVR_teacher_model.onnx  ends in Softmax -> already probabilities -> false
    //   act_trial_92.onnx            ends in Softmax -> already probabilities -> false
    //   natural_trial_96.onnx        ends in Softmax -> already probabilities -> false
    //   sound_model.onnx             ends in Gemm    -> raw logits            -> true
    //
    // `logmel_spectrogram.onnx` is a preprocessing transform, not a classifier, and is
    // loaded with `ModelLoader.Load` directly rather than through this helper.
    //
    // Appending a second softmax to a graph that already has one does not change argmax
    // (softmax is monotone) but it does destroy the confidence value: the teacher's
    // 7-class output was measured collapsing from a 0.84 peak to 0.27, which makes any
    // threshold or calibration fitted on it meaningless.
    internal static Model PrepareModel(ModelAsset asset, bool appendSoftmax)
    {
        var src = ModelLoader.Load(asset);

        if (!appendSoftmax)
        {
            return src;
        }

        var fg = new FunctionalGraph();
        var ins = fg.AddInputs(src);
        var outs = Functional.Forward(src, ins);
        var softmax = Functional.Softmax(outs[0]);

        fg.AddOutput(softmax);
        return fg.Compile();
    }
}
