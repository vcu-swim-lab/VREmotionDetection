# VREmotionDetection

Detect seven emotional expressions from Meta Quest Pro face tracking in your Unity game:
**Anger, Disgust, Fear, Happiness, Neutral, Sadness, and Surprise**.

## Two prediction options

| | Local model (default) | Jev |
|---|---|---|
| Runs | On the headset, offline | Through Typesafe's cloud API |
| Requires | The bundled ONNX model | Internet, a Typesafe account, and your API key |
| Service cost | No external inference charge | Third-party usage charges |
| Face information | Stays on the device | A description of face movements is sent to Typesafe |

**Our local model is trained on acted emotional-expression data**, using the MoE–TCN
(Mixture-of-Experts with Temporal Convolutional Networks) architecture described by Rahman et al.
in *Toward Generalized Emotion Recognition in VR by Bridging Natural and Acted Facial Expressions*
([paper](https://doi.org/10.3390/s26030845)). The bundled model uses an acted-only training
configuration; the paper's jointly trained models and reported results should not be treated as
performance claims for this release.

Jev uses the library's **one-step** approach: it converts tracked face movements into a description
and submits emotion questions to Jev. There is no separate generative-model step.

Predictions estimate expressions, not a person's actual feelings. They are **not medical diagnoses**.
Evaluate performance with your own users and application.

## Requirements

- Unity 6 (minimum declared version: `6000.0.33f1`).
- Meta Quest Pro with face tracking enabled and permission granted by the player.
- Dependencies declared by the package: Meta XR Core SDK `77.0.0`, Unity Inference Engine
  `2.2.1`, OpenXR `1.15.0`, and Newtonsoft Json `3.2.1`.
- For Jev: internet access and your own Typesafe credentials.

Check the [release notes](https://github.com/vcu-swim-lab/VREmotionDetection/releases)
for tested configurations. Android/Quest builds should use ARM64.

## Install and set up

1. In Unity, open **Window → Package Manager → Install package from Git URL**.
2. Enter:

   ```text
   https://github.com/vcu-swim-lab/VREmotionDetection.git
   ```

   To pin a version, append `#<published-tag>` or `#<commit-hash>`.
3. In the Meta project settings, enable **Face Tracking Support** as **Required** or **Supported**.
4. Add **New Face Au Model** to a GameObject. Its required tracking components are added automatically.
5. Confirm **Face Model** references the bundled ONNX asset in `Runtime/Models`.
   Leave **Use Calibration** off for your first test.

In Package Manager, import **Basic Usage**, **Movement Calibration**, **Calibration Check**, or
**Jev Prediction** from the package's Samples tab.

For additional installation options, see the [detailed setup guide](./Documentation~/UsageGuide.md#3-add-it-to-your-project).

## First prediction

Attach this script to the same GameObject as the predictor:

```csharp
using UnityEngine;
using VcuSwimLab.VREmotionDetection;

[RequireComponent(typeof(NewFaceAuModel))]
public class FirstPrediction : MonoBehaviour
{
    async void Start()
    {
        var predictor = GetComponent<NewFaceAuModel>();
        predictor.UseLocalModel(); // Already the default.

        var outcome = await predictor.TryPredict();
        if (outcome.Ok)
            Debug.Log($"{outcome.Prediction.Emotion}: {outcome.Prediction.Confidence:F2}");
        else
            Debug.LogWarning(outcome.Error);
    }
}
```

`TryPredict()` distinguishes a successful prediction from errors such as unavailable tracking.
For continuous predictions, use the [prediction-loop examples](./Documentation~/UsageGuide.md#8-recipes).

## How it works

The predictor collects five valid face-tracking readings at 10 Hz, passes the window to the selected
model, and returns seven emotion scores and a predicted label. Optional calibration adjusts the
decision using the player's saved reference expressions.

Collecting a window takes approximately half a second with valid tracking. Jev adds network
processing time; tracking loss and retries can increase the wait.

## Using Jev

Select Jev instead of the local model:

```csharp
using VcuSwimLab.Backends.Jev;

// Editor/desktop: explicitly read TYPESAFE_API_KEY from the environment.
predictor.UseJev(JevOptions.FromEnvironment());
```

On Quest, supply credentials through your application's secure runtime setup rather than relying
on desktop environment variables. For public deployments, use a server-side proxy to keep the
provider key out of the distributed build. Enable **Internet Access = Require** in Android Player Settings.

Jev is a **third-party paid service**. Do not embed API keys in scripts, scenes, prefabs, or builds.
See [Jev setup, credentials, and request options](./Documentation~/Jev.md).

## Optional personal calibration

Calibration records how one player makes each of the seven expressions. It creates personal
reference information; **it does not retrain the underlying model**.

1. Choose the model and set `predictor.UseCalibration = true`.
2. Call `LoadCalibration(playerId)`.
3. If it returns `false`, guide the player through `CalibrateAll(prompt)`.
4. After successful calibration, call `SaveCalibration(playerId)`.

On a later session, a compatible saved file for the same player and selected model lets the app
**reuse calibration without asking the player to repeat the expressions**. The file must still
exist in the app's storage on that device; it does not automatically transfer between devices.

Allow approximately 70 seconds plus prompts for the local model. Tracking failures can extend this;
Jev calibration also depends on network latency and retries. Improvement is not guaranteed.

See the [calibration guide](./Documentation~/Calibration.md) and
[complete calibration example](./Documentation~/UsageGuide.md#recipe-2--local-model-with-calibration).

## Privacy and limitations

- The local model runs on-device. Jev sends a description of face movements to Typesafe; optional
  `SendOwnFacesInRequest` also sends a calibration-derived summary.
- Obtain appropriate consent before using Jev. Follow your study protocol and platform requirements.
- Calibration profiles and results files stay in app storage. **Save Results is enabled by default**;
  turn it off if you do not need recording. Do not commit or share participant data.
- Acted expressions differ from spontaneous emotions. Tracking quality, headset fit, and differences
  between people can affect predictions. Neither backend guarantees accuracy.
- Review Typesafe's current [terms](https://typesafe.ai/legal/terms),
  [privacy policy](https://typesafe.ai/legal/privacy-policy), and pricing before deployment.
  Provider retention, deletion, service availability, and charges are controlled by Typesafe.

See [data flow and privacy details](./Documentation~/UsageGuide.md#16-privacy-data-and-limitations).
Report security/privacy concerns privately to [Kostadin Damevski](mailto:kdamevski@vcu.edu);
do not include keys or participant data in public issues.

## Documentation and research

- [Documentation index](./Documentation~/README.md)
- [Detailed examples and API reference](./Documentation~/UsageGuide.md)
- [Backend selection and custom backends](./Documentation~/Backends.md)
- [Errors and diagnostics](./Documentation~/Diagnostics.md)
- [Results and data formats](./Documentation~/DataFormats.md)
- [Migration guide](./Documentation~/Migration.md)
- [Related papers](./Documentation~/UsageGuide.md#17-related-papers-and-citation) and [BibTeX](./CITATIONS.bib)
- [Software citation](./CITATION.cff), [changelog](./CHANGELOG.md), and [third-party notices](./THIRD_PARTY_NOTICES.md)

The old `UseSparseVr()` method remains as a deprecated compatibility alias for `UseLocalModel()`.
Existing model filenames, asset references, and saved-calibration identifiers are unchanged.

## License

[MIT](./LICENSE). © 2026 Virginia Commonwealth University.
