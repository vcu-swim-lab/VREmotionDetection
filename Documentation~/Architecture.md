# Architecture

## Assemblies

```
VcuSwimLab.Calibration          plain C#, no Unity: the calibration maths
        ^
VcuSwimLab.Backends             contracts: IEmotionBackend, BackendRequest, BackendResult,
        ^   ^                   PredictionError, PredictionErrorCode, ScoreValidation, WindowValidation
        |   |
VcuSwimLab.Backends.SparseVr    VcuSwimLab.Backends.Jev
(bundled ONNX model)            (Typesafe Jev over HTTPS)
        ^                       ^
        +-----------+-----------+
                    |
VREmotionDetection              devices, NewFaceAuModel, CalibrationStore, results, legacy scripts
```

Arrows point from a dependency to the assembly that uses it. The two backend assemblies reference only the contracts (and the calibration types), never each other, so either one can be deleted or replaced without touching the other. Only `NewFaceAuModel` knows several backends.

| Assembly | Folder | What it owns |
|---|---|---|
| `VcuSwimLab.Calibration` | `Runtime/Calibration` | `CalibrationMath`, `MovementCalibrator`, the two centroid builders, `CalibrationProfile` |
| `VcuSwimLab.Backends` | `Runtime/Backends` | the backend contract and the error vocabulary |
| `VcuSwimLab.Backends.SparseVr` | `Runtime/Backends/SparseVr` | `SparseVrBackend`, `OnnxModelLoader`, `WindowTensor` |
| `VcuSwimLab.Backends.Jev` | `Runtime/Backends/Jev` | `JevBackend`, `JevOptions`, the HTTP seam, the prompt and request building |
| `VREmotionDetection` | `Runtime/Scripts` | `NewFaceAuModel`, `DeviceManager` and devices, `FacePrediction`, `CalibrationStore`, `ResultsRecorder`, `FaceAuModel` (obsolete), `SoundModel` (unfinished) |
| `VREmotionDetection.Editor` | `Editor` | the menu that copies results from a headset |

The existing public types (`NewFaceAuModel`, `FacePrediction`, `Emotion`, `DeviceManager`, `RingBuffer<T>` and the rest) stay in the global namespace so that existing projects compile unchanged. New types use the `VcuSwimLab.*` namespaces.

## A prediction, step by step

1. `DeviceManager` polls the face device at 10 Hz and passes a reading on only when the device calls it valid.
2. `FaceWindowSource` keeps the last 5 valid readings in a ring buffer. `TryPredict` clears it, waits for 5 fresh readings (3 s by default), and takes a deep copy.
3. `NewFaceAuModel` builds a `BackendRequest` from the window, the class names and (if calibrated) the player's profile, and calls the active backend.
4. The scores are validated (right count, finite, not negative) and the winner is chosen with `ScoreValidation.Argmax`.
5. If calibration is on and ready, `CalibratedDecision` may change the emotion. The confidence is the backend's own answer for the emotion that is returned.
6. The result is appended to the session CSV unless `saveResult` is false.

At most one prediction runs at a time per `NewFaceAuModel`. A second concurrent call returns a `Busy` error instead of corrupting the first one's window.

A prediction belongs to the backend and the enabled state that started it. If you switch backend or disable the component while it waits (for frames or for a backend), it ends with `Canceled`: its answer is dropped, nothing is recorded, and `LastError` and `PredictionFailed` are untouched. A backend that implements `ICancelableBackend` (Jev does) also stops its in-flight request and retries. Switching releases the busy state at once, so the next call is not blocked by the old one.

## Extension points

- A new model: implement `IEmotionBackend` and pass it to `UseBackend` (see [Backends](Backends.md)).
- A different network layer (a proxy, a test double): implement `IHttpTransport` and pass it to `JevBackend`. Implement `ICancelableHttpTransport` as well to let a cancel abort the request.
