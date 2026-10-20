using System;
using System.Linq;
using Unity.InferenceEngine;
using UnityEngine;

namespace VcuSeCenter.Backends.SparseVr
{
    public sealed class SparseVrBackend : IEmotionBackend
    {
        public const int WindowFrames = 5;
        public const int FeaturesPerFrame = 70;

        private readonly Worker worker;
        private bool inFlight;
        private bool disposed;

        public SparseVrBackend(ModelAsset asset, BackendType backendType = BackendType.GPUCompute)
        {
            if (asset == null)
            {
                throw new ArgumentNullException(nameof(asset));
            }

            Id = asset.name;
            worker = new Worker(OnnxModelLoader.Prepare(asset, appendSoftmax: false), backendType);
        }

        public string Id { get; }

        public float[] PrepareForCalibration(float[] scores) => scores;

        public async Awaitable<BackendResult> ScoreAsync(BackendRequest request)
        {
            var inference = await InferAsync(request.Window);
            if (inference.Output == null)
            {
                return BackendResult.Failure(inference.Failure.Value);
            }

            using (inference.Output)
            {
                float[] scores = inference.Output.DownloadToArray();
                int classCount = request.ClassNames == null ? scores.Length : request.ClassNames.Count;
                if (!ScoreValidation.TryValidate(scores, classCount, out string problem))
                {
                    return BackendResult.Failure(PredictionErrorCode.InvalidResponse, problem, Id);
                }

                return BackendResult.Success(scores);
            }
        }

        public async Awaitable<Tensor<float>> ScoreRawAsync(float[][] window)
        {
            var inference = await InferAsync(window);
            return inference.Output;
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            worker.Dispose();
        }

        private async Awaitable<Inference> InferAsync(float[][] window)
        {
            if (disposed)
            {
                return Inference.Fail(PredictionErrorCode.NotReady, "the SparseVr backend was disposed", Id);
            }

            if (!WindowValidation.TryValidate(window, WindowFrames, FeaturesPerFrame, out string problem))
            {
                return Inference.Fail(PredictionErrorCode.InvalidInput, problem, Id);
            }

            if (window.Length != WindowFrames)
            {
                return Inference.Fail(
                    PredictionErrorCode.InvalidInput, $"the window must have exactly {WindowFrames} frames", Id);
            }

            if (window.Any(frame => frame.Length != FeaturesPerFrame))
            {
                return Inference.Fail(
                    PredictionErrorCode.InvalidInput, $"every frame must have exactly {FeaturesPerFrame} values", Id);
            }

            if (inFlight)
            {
                return Inference.Fail(PredictionErrorCode.Busy, "the SparseVr model is already scoring a window", Id);
            }

            inFlight = true;
            try
            {
                using var input = WindowTensor.From(window);
                worker.Schedule(input);
                var output = await worker.PeekOutput().ReadbackAndCloneAsync() as Tensor<float>;
                return output == null
                    ? Inference.Fail(PredictionErrorCode.InvalidResponse, "the model returned no output", Id)
                    : new Inference(output, null);
            }
            catch (Exception e)
            {
                return Inference.Fail(PredictionErrorCode.Internal, $"{e.GetType().Name}: {e.Message}", Id);
            }
            finally
            {
                inFlight = false;
            }
        }

        private readonly struct Inference
        {
            public Inference(Tensor<float> output, PredictionError? failure)
            {
                Output = output;
                Failure = failure;
            }

            public Tensor<float> Output { get; }

            public PredictionError? Failure { get; }

            public static Inference Fail(PredictionErrorCode code, string message, string backendId)
            {
                return new Inference(null, new PredictionError(code, message, backendId));
            }
        }
    }
}
