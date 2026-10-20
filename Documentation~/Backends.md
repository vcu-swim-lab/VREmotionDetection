# Backends

A backend turns one window of face-tracking values into one score per emotion. The package ships two and lets you add your own.

The local model uses the MoE–TCN architecture described by Rahman et al., trained on acted emotional-expression data. See the [model description and paper](../README.md#two-prediction-options). Internal assembly and class names still contain `SparseVr` for compatibility; use `UseLocalModel()` in application code.

| Backend | Class | Runs | Id |
|---|---|---|---|
| Local model (default) | `SparseVrBackend` | on the device, with the bundled ONNX model | the model asset's name |
| Jev | `JevBackend` | in Typesafe's cloud | `jev:<model>:p<prompt version>`, for example `jev:jev-1.13.0:p1` |

There is **no fallback**. If the selected backend fails, the call returns a typed error; nothing quietly switches to another model.

## Selecting one

```csharp
predictor.UseLocalModel();                           // the default once a face model is assigned
predictor.UseJev(new JevOptions(apiKey));          // see Jev.md
predictor.UseBackend(myBackend);                   // your own IEmotionBackend
```

Switching clears any current calibration, because a calibration belongs to the backend that made it. The predictor disposes a backend it replaced, except the local model backend, which it keeps so you can switch back. A scene with no face model assigned works with `UseJev` or `UseBackend` alone.

To compare backends on the same face, score one window with another backend without switching:

```csharp
var other = predictor.CreateJevBackend(options);
var result = await predictor.ScoreWith(other, prediction.Window);
```

## The contract

```csharp
public interface IEmotionBackend : IDisposable
{
    string Id { get; }
    Awaitable<BackendResult> ScoreAsync(BackendRequest request);
    float[] PrepareForCalibration(float[] scores);
}
```

- `BackendRequest` carries `Window` (frames, oldest first), `ClassNames` (the emotions in output order) and `ReferenceProfile` (the player's calibration, or null). A backend may use the profile or ignore it.
- `BackendResult` is either scores or an error. `Scores` has one value per class name, in class order, finite and not negative. `Raw` is optional: the backend's own answers before any normalisation. `ReferenceFacesSent` says whether the profile reached the model.
- `Id` is stored inside saved calibrations. Change it whenever a change would make old calibrations meaningless (a retrained model, a new prompt).
- `PrepareForCalibration` lets a backend make its scores safe for the calibration's logarithm. Local model returns them unchanged; Jev adds a small floor because it often answers exactly 0.
- Optional: implement `ICancelableBackend` (`void CancelPending()`) if the backend can stop work in flight. The predictor calls it when you switch away, disable or destroy the component. Backends without it keep working; their late answers are dropped.
- Return errors with `BackendResult.Failure(code, message, Id)`. Do not throw for conditions such as no network; reserve exceptions for programming mistakes. If a backend does throw, the predictor catches it and reports an `Internal` error.

## Writing a backend

```csharp
using UnityEngine;
using VcuSeCenter.Backends;
using VcuSeCenter.VREmotionDetection;

public sealed class ConstantBackend : IEmotionBackend
{
    public string Id => "constant";

    public async Awaitable<BackendResult> ScoreAsync(BackendRequest request)
    {
        if (!WindowValidation.TryValidate(request.Window, 1, 63, out string problem))
        {
            return BackendResult.Failure(PredictionErrorCode.InvalidInput, problem, Id);
        }

        var scores = new float[request.ClassNames.Count];
        scores[(int)Emotion.Neutral] = 1f;
        return BackendResult.Success(scores);
    }

    public float[] PrepareForCalibration(float[] scores) => scores;

    public void Dispose() { }
}
```

For this example, reference both `VcuSeCenter.Backends` and `VREmotionDetection` in your custom assembly definition: `Emotion.Neutral` is defined in the runtime assembly. Unity engine references must also be enabled for `Awaitable`.

A backend that must remain independent of the runtime assembly should use the supplied `request.ClassNames` to identify output indices instead of the runtime's `Emotion` enum. Such a backend can reference only the contracts assembly (`VcuSeCenter.Backends`), plus `VcuSeCenter.Calibration` if it uses profile types, without referencing either concrete backend. Put the example above in a separate application assembly, not in the package's backend-contract assembly.

## Score conventions

| | Local model | Jev |
|---|---|---|
| Scores | a probability distribution (sums to 1) from the model's own softmax | the seven independent yes/no answers divided by their sum |
| Raw | none | the seven answers as Jev gave them |
| Confidence returned by `Predict` | the probability of the chosen emotion | Jev's own answer for the chosen emotion |
