# Third-party notices

VREmotionDetection is licensed under the MIT License (see [LICENSE](./LICENSE)). This file lists
everything else the package uses. Nothing under "Dependencies" is copied into this repository:
Unity's Package Manager installs those packages from their publishers, under their own licences.

## Included in the package

### Bundled model

`Runtime/Models/sparseVR_teacher_model_acted_only_vol-aug-k1.onnx`

| | |
|---|---|
| Used for | the local-model backend (on-device emotion prediction) |
| Owner and licence | trained by the VCU SWIM Lab and distributed under this package's MIT licence |
| Training data | acted facial expressions recorded with Meta Quest Pro headsets, and the EmojiHeroVR database (EmoHeVRDB). See [CITATIONS.bib](./CITATIONS.bib) |

No recordings, participant data or calibration profiles are included in this repository.

## Dependencies installed by Unity's Package Manager

| Component | Version | Licence | Used for |
|---|---|---|---|
| Unity Inference Engine (`com.unity.ai.inference`) | 2.2.1 | Unity Terms of Service, see the package's `LICENSE.md` and `Third Party Notices.md` | runs the local model |
| Meta XR Core SDK (`com.meta.xr.sdk.core`) | 77.0.0 | Oculus SDK License Agreement, <https://developer.oculus.com/licenses/oculussdk/> | Quest Pro face tracking (`OVRFaceExpressions`) |
| OpenXR Plugin (`com.unity.xr.openxr`) | 1.15.0 | Unity Companion License for the source code, otherwise the Unity Package Distribution License | XR runtime on the headset |
| Newtonsoft Json for Unity (`com.unity.nuget.newtonsoft-json`) | 3.2.1 | Unity Companion License; it contains Newtonsoft.Json, MIT licence, Copyright (c) 2007 James Newton-King | Jev request and response (JSON) |
| `com.unity.modules.jsonserialize`, `com.unity.modules.unitywebrequest` | 1.0.0 | part of the Unity engine, under your Unity licence | saving files, Jev requests |

## External service (not part of the package)

**Jev, by Typesafe AI**, is used only if your game chooses the Jev backend. It is a third-party paid
service that you use with your own account and API key. The package contains no Typesafe code and no
key. Typesafe's [Terms of Use](https://typesafe.ai/legal/terms) and
[Privacy Policy](https://typesafe.ai/legal/privacy-policy) apply, and the service is documented at
<https://docs.typesafe.ai>. What the package sends to the service is described in the README, section
"Privacy, data and limitations".

## Questions

Kostadin Damevski, [kdamevski@vcu.edu](mailto:kdamevski@vcu.edu).
