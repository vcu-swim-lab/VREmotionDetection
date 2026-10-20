using System;
using UnityEngine;

namespace VcuSeCenter.Backends
{
    public interface IEmotionBackend : IDisposable
    {
        string Id { get; }

        Awaitable<BackendResult> ScoreAsync(BackendRequest request);

        float[] PrepareForCalibration(float[] scores);
    }
}
