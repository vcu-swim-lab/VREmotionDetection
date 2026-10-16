# Changelog

## 2.2.0

### Added
- Pluggable backends behind `IEmotionBackend`: the bundled SparseVr ONNX model and a new Jev (Typesafe) backend. `UseSparseVr`, `UseJev`, `UseBackend`, `ScoreWith`.
- `JevOptions`: the API key is a constructor parameter; `FromEnvironment()` is an explicit opt-in.
- Typed errors: `PredictionErrorCode`, `PredictionError`, `TryPredict`, `LastError`, `PredictionFailed`.
- Calibration follows the active backend and is tied to it when saved.
- `CalibrateAll`, `CalibrationStore` (atomic writes, custom folder), `FacePrediction.Raw`/`ScoreOf`/`BackendId`.
- `backend_error` rows in the results file.
- Documentation (`Documentation~`), a Jev sample, a `dotnet` test harness (`Tests~/DotnetHarness`).

### Changed
- The first prediction waits one frame for the predictor to start, so a script's own `Start` can call `Predict` straight away.
- The code is split into five assemblies with one-way dependencies.
- `DeviceManager` creates devices on request; the microphone is no longer started at start-up.
- Concurrent predictions return `Busy`; invalid arguments throw; calibration calls are no-ops when calibration is off.
- The headset results menu reports the files it copied and refuses to guess between several devices.
- `package.json` completed (Unity 6, keywords, links, module dependencies).
- All source comments moved into the documentation.

### Fixed
- Calibration calls made before the predictor's `Start` (`LoadCalibration`, `SaveCalibration`, `StartCalibration`) work again, and the calibration survives the start.
- The results recorder reopened its file after being disposed and could lose the header.
- Re-enabling the component left a disposed model.
- `userId` validation accepted a trailing newline.
- A download that failed after the response headers arrived was read as a successful answer.
- A custom backend that threw could make `Predict` throw.
- The calibration summary was copied for every prediction; it is now built once per calibration.
- On Android the microphone was started before the permission dialog was answered.

## 2.1.0
- Acted-data model, optional movement calibration, results saving.
