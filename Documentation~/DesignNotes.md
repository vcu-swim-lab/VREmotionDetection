# Design notes

The reasons behind choices that are not obvious from the code.

## Models and scores

- **No extra softmax.** The face models already end in a Softmax. Appending a second one does not change the winner, but it ruins the confidence: the teacher's 7-class peak was measured falling from 0.84 to 0.27, which makes any threshold or calibration fitted on it meaningless. `OnnxModelLoader.Prepare` therefore requires you to say whether to append one. The voice model (archived) ends in raw logits and does need it.
- **Confidence is the backend's own answer.** For the local model that is a probability. For Jev, whose seven answers are independent, dividing by their sum would make a single weak answer read as 100%.
- **Scores are validated before use.** A model with the wrong number of outputs, or a NaN, becomes an `InvalidResponse` error. Without that a NaN would silently pick Anger (index 0).
- **Timeout of 3 seconds.** A window needs 0.5 s of valid frames at 10 Hz; the rest allows a few seconds of lost tracking before the caller is told the prediction could not be made.

## Face data

- **Invalid readings are dropped, never zero-filled.** When tracking is lost the buffer would hold all zeros, and a real face never produces 70 exact zeros. Without the gate a dropout was indistinguishable from a genuinely neutral face once it reached the model.
- **Deep-copied windows, rotated arrays.** The ring buffer rotates its backing arrays instead of copying references, so every timestep has its own array; and a snapshot is a deep copy, because the arrays are recycled on every push. Building the tensor from the snapshot guarantees that a logged window is byte for byte what the model saw.
- **Devices are created on request.** `DeviceManager` used to construct every device class at start-up to read its input spec, which started the microphone (and asked for microphone permission on a Quest) in every app that used face tracking. Devices now come from an explicit registry and are created by `Require`. Test doubles are never created by the manager.
- **Sampling follows real time.** Devices are polled against `Time.unscaledTime`, and calibration sample spacing waits on it too, because the headset delivers faces regardless of `Time.timeScale` and the model expects about 10 readings a second. Pausing the game therefore does not stop face tracking; stop calling `Predict` or disable the component to pause detection.
- **Devices are released with their manager.** When `DeviceManager` is destroyed every device is released; when it is only disabled, a device that holds hardware (the microphone) is stopped and created again by the next `Require`, while the face device stays because the window source listens to it.
- **Polling is isolated per device.** An exception from one device's listener is caught, reported once, and the device retried after a second, instead of stopping every device each frame.

## Calibration

See [Calibration](Calibration.md) for the frozen constants. Also:

- The first sample of an emotion is dropped (taken while the face is still changing), nine are kept; a missing sample is not counted, so the caller simply tries again; the calibration either completes for every emotion or reports why not, never a partial set.
- A fault in a sample (a non-finite value) is remembered and the first one wins, because later ones are usually the same cause reported again.
- `ObserveAt` exists for fixed-clock protocols where a missed tick must stay missed.
- A calibration is tied to the backend id so that a profile built from one model's scores is never applied to another's.
- `userId` is restricted to letters, digits, `-` and `_` because it becomes a file name.
- Calibration is stored per device and never in an asset or a repository, because it describes one person's face.

## Results and the editor menu

- Saving results must never stop the game, so write errors are logged and swallowed.
- The recorder separates `Close` (pause; reopen allowed) from `Dispose` (final), so a prediction that finishes after the component is destroyed cannot reopen the file and leak a handle.
- `InvariantCulture` everywhere in the CSV so `0.75` is the same on every device.
- The menu writes a `.gitignore` into `HeadsetResults` because the files describe real people.

## Internal types

`Device`, `DeviceManager.PollAndDispatch`, `DeviceRegistry`, the Jev prompt and parsing classes and similar are internal on purpose: they are implementation details, not public surface. Only the editor assembly can see the runtime's internals (`StoragePaths`), through `InternalsVisibleTo`.
