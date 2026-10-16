# Diagnostics and error handling

## The rule

- **Programming mistakes throw.** A bad argument (`ArgumentException`, `ArgumentOutOfRangeException`, `ArgumentNullException`) or a call in the wrong order (`InvalidOperationException`).
- **Conditions at run time return a typed error.** Tracking lost, no network, a rejected key, a model that misbehaves.

A failed prediction never throws and never falls back to another model. That includes a custom backend: if its `ScoreAsync` throws, or returns a result with neither scores nor an error, the predictor reports `Internal` or `InvalidResponse` instead of letting the exception escape.

## Reading a failure

```csharp
var outcome = await predictor.TryPredict();
if (!outcome.Ok)
{
    PredictionError error = outcome.Error.Value;
    Debug.LogWarning(error);                       // "<backend id> <Code>: <message>"
    if (error.Code == PredictionErrorCode.Unauthorized) { /* ask for a new key */ }
    if (error.IsTransient) { /* try again later */ }
}
```

The older calls still work: `PredictDetailed` returns null on failure, and `Predict` returns `(Neutral, 0)`, which is also a real neutral reading with zero confidence, so use `TryPredict` when the difference matters. `predictor.LastError` holds the most recent failure (null after a success), and `predictor.PredictionFailed` is raised for each one. A handler that throws is caught and logged; it cannot break prediction.

## Codes

| Code | Meaning | Usual cause |
|---|---|---|
| `NotConfigured` | no backend is selected | no face model assigned and no `UseJev` or `UseBackend` call |
| `NotReady` | the predictor is not started | called before `Start`, or while disabled |
| `Busy` | another prediction is running | two overlapping calls on one predictor |
| `NoTracking` | no valid window in time | face tracking lost or never started |
| `InvalidInput` | the window or request is unusable | wrong shape, NaN values |
| `Unauthorized` | the key was rejected | missing, expired or wrong API key |
| `InvalidRequest` | the service rejected the request | wrong model name, bad payload |
| `RateLimited` | too many requests | slow down; `IsTransient` |
| `Timeout` | no answer in time | slow network; `IsTransient` |
| `Unreachable` | no connection | offline, DNS, firewall; `IsTransient` |
| `ServerError` | the service failed | try again later; `IsTransient` |
| `InvalidResponse` | the answer cannot be used | wrong number of scores, NaN, malformed body |
| `Internal` | an unexpected exception | a bug or a failing model |

`PredictRaw` returns null (no tensor, no error) when the SparseVr model is unavailable, the window times out, or a prediction is already running.

`NotReady`, `NotConfigured` and `Busy` answers take one frame, so a loop that calls `Predict` in a `while` cannot spin and freeze the editor.

## Logging

One warning per distinct message, not one per call: a lost-tracking loop logs once until a prediction succeeds. No log line or error message ever contains the API key. Polling failures in `DeviceManager` are also reported once per device, and the device is retried after a second.

## Results file

Failed predictions appear in the CSV as `no_tracking` (tracking was lost) or `backend_error` (the backend failed). `NotReady` and `Busy` are caller mistakes and are not recorded. See [Data formats](DataFormats.md).

## Checking before you run

`UseJev(null)` and `new JevOptions("")` throw immediately. `JevOptions.Validate()` lists every problem with the settings at once. `LoadCalibration` and `SaveCalibration` report file problems through `CalibrationError` and a false return; only an invalid `userId` throws.
