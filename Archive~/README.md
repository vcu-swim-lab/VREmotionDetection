# Archive~

Models the library no longer uses, kept for the record. Unity skips folders whose names end in
`~`, so nothing here is imported into a project or included in a build. Each model keeps its
`.meta` file, so moving it back into `Runtime/Models` restores it with the same asset id.

| Model | What it was |
|---|---|
| `natural_trial_96.onnx`, `act_trial_92.onnx` | The natural-data and acted-data pair used by the obsolete `FaceAuModel` |
| `sparseVR_teacher_model.onnx` | The first teacher model, trained on natural and acted data |
| `sparseVR_teacher_model_acted_only.onnx` | The acted-data teacher before volume augmentation |
| `sound_model.onnx`, `logmel_spectrogram.onnx` | The unfinished voice predictor (`SoundModel`): the log-mel converter and the voice emotion model. `sound_model` ends in raw logits, so `SoundModel` appends a softmax |

The only model in use is `Runtime/Models/sparseVR_teacher_model_acted_only_vol-aug-k1_with-EmojiHero.onnx`
(acted data only, volume augmentation, with EmojiHero).
