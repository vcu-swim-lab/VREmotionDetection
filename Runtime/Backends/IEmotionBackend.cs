using System;
using UnityEngine;

namespace VcuSwimLab.Backends
{
    public interface IEmotionBackend : IDisposable
    {
        string Id { get; }

        Awaitable<BackendResult> ScoreAsync(BackendRequest request);

        float[] PrepareForCalibration(float[] scores);
    }
}
