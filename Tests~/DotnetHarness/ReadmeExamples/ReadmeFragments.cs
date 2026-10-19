using System.Threading.Tasks;
using UnityEngine;
using VcuSwimLab.Backends;
using VcuSwimLab.Backends.Jev;
using VcuSwimLab.VREmotionDetection;

// Snippets from README.md and Documentation~/UsageGuide.md that are not whole classes. Compile check only.
public class ReadmeFragments : MonoBehaviour
{
    private NewFaceAuModel predictor;

    // UsageGuide, top of the guide.
    public async Task Hero()
    {
        var (emotion, confidence) = await predictor.Predict();      // Happiness, 0.81
    }

    // README, "First prediction": body of the Start() method.
    public async Task ReadmeFirstPrediction()
    {
        predictor.UseLocalModel(); // Already the default.

        var outcome = await predictor.TryPredict();
        if (outcome.Ok)
            Debug.Log($"{outcome.Prediction.Emotion}: {outcome.Prediction.Confidence:F2}");
        else
            Debug.LogWarning(outcome.Error);
    }

    // README, "Reading the result".
    public async Task ReadmeReadingTheResult()
    {
        var outcome = await predictor.TryPredict();
        if (outcome.Ok)
        {
            var prediction = outcome.Prediction;
            var emotion = predictor.CalibratedEmotion(prediction) ?? prediction.Emotion;
            Debug.Log($"{emotion}: {prediction.ScoreOf(emotion):F2}");
        }
        else
        {
            Debug.LogWarning(outcome.Error);
        }
    }

    // README, Jev setup (Editor/desktop).
    public void ReadmeJevFromEnvironment()
    {
        predictor.UseJev(JevOptions.FromEnvironment());
    }

    // UsageGuide, "Jev in depth".
    public async Task JevOptionsFragment(string apiKey)
    {
        var options = new JevOptions(apiKey);              // required
        options.Model = "jev-1.13.0";                      // default; pin a versioned model
        options.TimeoutSeconds = 10;                       // per attempt
        options.MaxRetries = 2;                            // transient failures only
        options.SendOwnFacesInRequest = false;             // default
        options.LogRequests = false;                       // true: one Console line per request
        options.IncludeServerDetails = false;              // true: add Typesafe's own error text to messages
        predictor.UseJev(options);
    }

    // UsageGuide, "Write your own backend".
    public void CustomBackendFragment()
    {
        predictor.UseBackend(new AlwaysNeutralBackend());
    }

    // The deprecated alias must keep compiling for existing projects.
    public void DeprecatedUseSparseVrStillCompiles()
    {
        predictor.UseSparseVr();
    }
}
