# Data formats

## Model input and output (Local model)

| | |
|---|---|
| Values per frame | 70: the `OVRFaceExpressions` face-tracking weights in `FaceExpression` order (`BrowLowererL` to `TongueRetreat`) |
| Range | raw weights, 0 to 1; no scaling or normalisation |
| Window | 5 consecutive frames, oldest first, read at 10 Hz (half a second) |
| Missing tracking | frames without valid tracking are dropped, never replaced with zeros |
| Input tensor | `[1, 5, 70]`, float32 |
| Output | 7 probabilities that sum to 1, in `Emotion` order: Anger, Disgust, Fear, Happiness, Neutral, Sadness, Surprise |

The model was trained on acted expressions recorded on the Quest Pro with 5-frame windows at about 10 Hz. The last 7 of the 70 values are tongue values; the calibration and Jev use only the first 63.

## `FacePrediction`

| Field | Meaning |
|---|---|
| `Emotion` | the winning emotion |
| `Confidence` | the backend's own answer for `Emotion` (a probability for the local model, Jev's raw answer for Jev) |
| `Scores` | one value per emotion, in `Emotion` order |
| `Raw` | the backend's answers before normalisation; null for the local model |
| `Window` | an independent deep copy of the frames the backend saw, `[frames][features]` |
| `Generation` | how many valid frames have ever arrived when the window was taken |
| `BackendId` | which backend produced it |
| `ScoreOf(emotion)` | the answer for one emotion (`Raw` when present, else `Scores`) |

`Window` is a copy because the ring buffer reuses its arrays on every poll; a reference would have changed by the time you logged it. Logging it lets you replay the exact input through the same model offline. The difference between the `Generation` of two predictions is the number of valid frames that arrived in between: a smaller step than expected means tracking dropped out, and the buffer's counter is never reset by `Clear` for exactly that reason.

## Results file

When **Save Results** is on (the default) every prediction is appended to one CSV per predictor, opened at its first prediction.

`Application.persistentDataPath/VREmotionDetection/results/session_<yyyyMMdd_HHmmss>.csv` (UTC; a suffix `_2`, `_3` is added if the name is taken).

```
time_utc,seconds,emotion,confidence,calibrated,model_emotion
2026-10-12T14:03:05.120Z,12.004,Happiness,0.8123,yes,Surprise
2026-10-12T14:03:06.131Z,13.015,no_tracking,,,
2026-10-12T14:03:07.140Z,14.024,backend_error,,,
```

- `time_utc`: `yyyy-MM-ddTHH:mm:ss.fffZ`. `seconds`: since the first row, from a monotonic clock, 3 decimals.
- `emotion`: what your game received. `model_emotion`: the backend's own answer; they differ only when calibration changed it. `calibrated`: `yes` or `no`.
- `confidence`: 4 decimals, with `.` as the decimal separator on every device. For Jev it is Jev's raw answer.
- `no_tracking` and `backend_error` rows have empty value columns.
- UTF-8 without a byte-order mark, so Excel, pandas and csv readers see the real first column name.
- The file never says who played, and raw face-tracking values are never saved. Calibration samples are not saved.
- Saving never stops the game: a failed write is logged and the prediction carries on. On a Quest the app pauses instead of quitting when the headset comes off, so the file is closed on pause and the next prediction appends to the same file.

`predictor.GetResultsSummary()` returns text such as `120 predictions over 2.1 min`, the share and average confidence per emotion, and the failure counts. `NewFaceAuModel.DeleteSavedResults()` deletes every results file on the device.

Copy the files to a PC with the Unity menu **VR Emotion > Get Results From Headset** (uses `adb` from Unity's Android Build Support; the headset must be the only connected device). It writes a `.gitignore` next to the files because they describe real people.

## Calibration file

`Application.persistentDataPath/VREmotionDetection/calibration/<userId>.json`, written with `JsonUtility`:

| Field | Content |
|---|---|
| `implementationVersion` | the calibration implementation, `runtime-calibration-v1` |
| `faceModel` | the backend id that made it (the model asset name for the local model) |
| `savedUtc` | when it was saved (ISO 8601) |
| `classNames` | the emotions in order |
| `scoreWidth`, `scoreCentroids` | 7, and the 7x7 matrix flattened row by row |
| `movementWidth`, `movementCentroids` | 63, and the 7x63 matrix flattened row by row |

Other tools may read this layout, so the field names are stable.
