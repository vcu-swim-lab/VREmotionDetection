using System;
using Unity.InferenceEngine;
using UnityEngine;
using VcuSwimLab.Backends;
using VcuSwimLab.Backends.Jev;
using VcuSwimLab.Backends.SparseVr;
using VcuSwimLab.Calibration;

[RequireComponent(typeof(OVRFaceExpressions))]
[RequireComponent(typeof(DeviceManager))]
public class NewFaceAuModel : MonoBehaviour
{
    public const int WindowFrames = SparseVrBackend.WindowFrames;
    public const int FeaturesPerFrame = SparseVrBackend.FeaturesPerFrame;
    public const float DefaultTimeoutSeconds = 3f;

    public const string DefaultModelPath =
        "Packages/com.vcu-swim-lab.vr-emotion-detection/Runtime/Models/" +
        "sparseVR_teacher_model_acted_only_vol-aug-k1_with-EmojiHero.onnx";

    private const string CalibrationOffMessage =
        "calibration is off: tick \"Use Calibration\" on the predictor, or set UseCalibration = true";

    private static readonly string[] ClassNames = Enum.GetNames(typeof(Emotion));

    [SerializeField]
    private ModelAsset faceModel;

    [Tooltip("Adapt the predictions to each player with movement calibration. Off by default: " +
             "the backend's own answer is used and the calibration methods do nothing. When on, " +
             "the game runs the calibration (CalibrateEmotion for each emotion, or " +
             "LoadCalibration for a saved one) and Predict uses it.")]
    [SerializeField]
    private bool useCalibration = false;

    [Tooltip("Save every prediction of the session to a CSV file on the device. Copy the files " +
             "to your PC with the Unity menu VR Emotion > Get Results From Headset.")]
    [SerializeField]
    private bool saveResults = true;

#if UNITY_EDITOR
    void Reset()
    {
        if (faceModel == null)
        {
            faceModel = UnityEditor.AssetDatabase.LoadAssetAtPath<ModelAsset>(DefaultModelPath);
        }
    }
#endif

    private readonly CalibrationStore calibrationStore = new CalibrationStore();
    private readonly ResultsRecorder results = new ResultsRecorder();

    private FaceWindowSource windowSource;
    private SparseVrBackend sparseVr;
    private IEmotionBackend backend;
    private MovementCalibrator calibrator;
    private string calibrationBackendId;
    private MovementCalibrator referenceSource;
    private CalibrationProfile referenceProfile;
    private string calibrationError;
    private PredictionError? lastError;
    private string lastLoggedError;
    private bool started;
    private bool busy;

    public event Action<PredictionError> PredictionFailed;

    public string FaceModelName => faceModel != null ? faceModel.name : null;

    public string ActiveBackendId => backend?.Id;

    private string CurrentBackendId => backend != null ? backend.Id : faceModel != null ? faceModel.name : null;

    public PredictionError? LastError => lastError;

    public bool UseCalibration
    {
        get => useCalibration;
        set => useCalibration = value;
    }

    public bool IsCalibrated => useCalibration && calibrator != null && calibrator.IsReady;

    public string CalibrationError => calibrationError ?? calibrator?.Error;

    public string ResultsFilePath => results.FilePath;

    public static string ResultsFolder => ResultsRecorder.Folder;

    public void Start()
    {
        if (started)
        {
            return;
        }

        started = true;
        Initialise();
    }

    void OnEnable()
    {
        if (started)
        {
            Initialise();
        }
    }

    void OnDisable()
    {
        windowSource?.Dispose();
        windowSource = null;

        if (sparseVr != null)
        {
            if (ReferenceEquals(backend, sparseVr))
            {
                backend = null;
            }

            sparseVr.Dispose();
            sparseVr = null;
        }

        results.Close();
    }

    void OnDestroy()
    {
        if (backend != null && !ReferenceEquals(backend, sparseVr))
        {
            backend.Dispose();
        }

        backend = null;
        results.Dispose();
    }

    void OnApplicationPause(bool paused)
    {
        if (paused)
        {
            results.Close();
        }
    }

    public void UseSparseVr()
    {
        if (faceModel == null)
        {
            throw new InvalidOperationException("assign a face model asset to use the SparseVr backend");
        }

        if (sparseVr == null)
        {
            sparseVr = new SparseVrBackend(faceModel);
        }

        Activate(sparseVr);
    }

    public void UseJev(JevOptions options)
    {
        Activate(CreateJevBackend(options));
    }

    public IEmotionBackend CreateJevBackend(JevOptions options)
    {
        if (options == null)
        {
            throw new ArgumentNullException(nameof(options));
        }

        return new JevBackend(options, FaceAuNames.Movement);
    }

    public void UseBackend(IEmotionBackend newBackend)
    {
        if (newBackend == null)
        {
            throw new ArgumentNullException(nameof(newBackend));
        }

        Activate(newBackend);
    }

    public async Awaitable<PredictionOutcome> TryPredict(float timeoutSeconds = DefaultTimeoutSeconds,
                                                         bool saveResult = true)
    {
        var outcome = await PredictCore(timeoutSeconds);
        if (saveResult)
        {
            RecordResult(outcome);
        }

        return outcome;
    }

    public async Awaitable<FacePrediction?> PredictDetailed(float timeoutSeconds = DefaultTimeoutSeconds,
                                                            bool saveResult = true)
    {
        var outcome = await TryPredict(timeoutSeconds, saveResult);
        return outcome.PredictionOrNull;
    }

    public async Awaitable<(Emotion, float)> Predict()
    {
        var outcome = await TryPredict();
        if (!outcome.Ok)
        {
            return (Emotion.Neutral, 0f);
        }

        var emotion = CalibratedEmotion(outcome.Prediction) ?? outcome.Prediction.Emotion;
        return (emotion, outcome.Prediction.ScoreOf(emotion));
    }

    public async Awaitable<Tensor<float>> PredictRaw()
    {
        if (!started || windowSource == null || sparseVr == null || busy)
        {
            await Awaitable.NextFrameAsync();
            return null;
        }

        busy = true;
        try
        {
            var window = await windowSource.AwaitWindowAsync(DefaultTimeoutSeconds);
            return window == null ? null : await sparseVr.ScoreRawAsync(window);
        }
        finally
        {
            busy = false;
        }
    }

    public async Awaitable<BackendResult> ScoreWith(IEmotionBackend other, float[][] window)
    {
        if (other == null)
        {
            throw new ArgumentNullException(nameof(other));
        }

        try
        {
            return await other.ScoreAsync(new BackendRequest(window, ClassNames, ReferenceProfile()));
        }
        catch (Exception e)
        {
            return BackendResult.Failure(PredictionErrorCode.Internal, $"{e.GetType().Name}: {e.Message}", other.Id);
        }
    }

    public void StartCalibration()
    {
        calibrator = null;
        calibrationBackendId = null;
        calibrationError = null;

        if (!useCalibration)
        {
            calibrationError = CalibrationOffMessage;
            Debug.LogWarning($"{nameof(NewFaceAuModel)}: {CalibrationOffMessage}");
            return;
        }

        calibrator = new MovementCalibrator(ClassNames);
        calibrationBackendId = CurrentBackendId;
    }

    public void ClearCalibration()
    {
        calibrationError = null;
        calibrator = null;
        calibrationBackendId = null;
    }

    public async Awaitable<bool> CalibrateEmotion(Emotion emotion, float secondsBetweenSamples = 1f,
                                                  int maxAttempts = 20)
    {
        if (!useCalibration)
        {
            return false;
        }

        if (calibrator == null)
        {
            throw new InvalidOperationException("call StartCalibration before CalibrateEmotion");
        }

        if (secondsBetweenSamples < 0f)
        {
            throw new ArgumentOutOfRangeException(nameof(secondsBetweenSamples), secondsBetweenSamples, "must be 0 or more");
        }

        if (maxAttempts < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maxAttempts), maxAttempts, "must be 1 or more");
        }

        var session = calibrator;
        string calibratingBackendId = CurrentBackendId;
        session.BeginEmotion((int)emotion);

        for (int attempt = 0; !session.IsEmotionComplete((int)emotion); ++attempt)
        {
            if (attempt >= maxAttempts)
            {
                calibrationError = $"could not collect enough samples for {emotion} in {maxAttempts} attempts: " +
                                   "face tracking was lost or the backend kept failing (see LastError)";
                return false;
            }

            float nextSample = Time.time + secondsBetweenSamples;

            var outcome = await PredictCore(DefaultTimeoutSeconds);
            if (!ReferenceEquals(session, calibrator) || calibratingBackendId != CurrentBackendId)
            {
                calibrationError = "the calibration was cleared or the backend changed while it was running";
                return false;
            }

            if (outcome.Ok)
            {
                session.Observe(CalibrationScores(outcome.Prediction), outcome.Prediction.Window);
            }

            float remaining = nextSample - Time.time;
            if (remaining > 0)
            {
                await Awaitable.WaitForSecondsAsync(remaining);
            }
        }

        return true;
    }

    public async Awaitable<bool> CalibrateAll(Func<Emotion, Awaitable> prompt = null,
                                              float secondsBetweenSamples = 1f, int maxAttempts = 20)
    {
        StartCalibration();
        if (!useCalibration)
        {
            return false;
        }

        foreach (Emotion emotion in Enum.GetValues(typeof(Emotion)))
        {
            if (prompt != null)
            {
                await prompt(emotion);
            }

            if (calibrator == null)
            {
                calibrationError = "the calibration was cleared or the backend changed while it was running";
                return false;
            }

            if (!await CalibrateEmotion(emotion, secondsBetweenSamples, maxAttempts))
            {
                return false;
            }
        }

        return FinishCalibration();
    }

    public bool FinishCalibration()
    {
        return calibrator != null && calibrator.Finish();
    }

    public void BeginCalibrationEmotion(Emotion emotion)
    {
        if (!useCalibration)
        {
            return;
        }

        if (calibrator == null)
        {
            throw new InvalidOperationException("call StartCalibration before BeginCalibrationEmotion");
        }

        calibrator.BeginEmotion((int)emotion);
    }

    public bool AddCalibrationSample(int position, FacePrediction? prediction)
    {
        if (!useCalibration)
        {
            return false;
        }

        if (calibrator == null)
        {
            throw new InvalidOperationException("call StartCalibration before AddCalibrationSample");
        }

        float[] scores = prediction.HasValue ? CalibrationScores(prediction.Value) : null;
        return calibrator.ObserveAt(position, scores, prediction?.Window);
    }

    public int[] CalibrationSampleCounts() => calibrator?.KeptSampleCounts();

    public NearestCentroid? CalibratedDecision(FacePrediction prediction)
    {
        if (!useCalibration)
        {
            return null;
        }

        float[] scores = CalibrationScores(prediction);
        return scores == null ? null : calibrator?.Classify(scores, prediction.Window);
    }

    public Emotion? CalibratedEmotion(FacePrediction prediction)
    {
        var decision = CalibratedDecision(prediction);
        return decision.HasValue ? (Emotion)decision.Value.ClassIndex : (Emotion?)null;
    }

    public static string CalibrationPath(string userId) => CalibrationStore.DefaultPathFor(userId);

    public bool HasSavedCalibration(string userId) => calibrationStore.Exists(userId);

    public bool SaveCalibration(string userId)
    {
        var profile = calibrator?.ExportProfile();
        string backendId = CurrentBackendId;
        if (profile == null || backendId == null)
        {
            return false;
        }

        if (!calibrationStore.TrySave(userId, backendId, profile, out string error))
        {
            calibrationError = error;
            return false;
        }

        return true;
    }

    public bool LoadCalibration(string userId)
    {
        calibrator = null;
        calibrationBackendId = null;
        calibrationError = null;

        if (!useCalibration)
        {
            calibrationError = CalibrationOffMessage;
            return false;
        }

        string backendId = CurrentBackendId;
        if (backendId == null)
        {
            calibrationError = "no backend is selected, so a saved calibration cannot be matched to it";
            return false;
        }

        if (!calibrationStore.TryLoad(userId, backendId, out CalibrationProfile profile, out string error))
        {
            calibrationError = error;
            return false;
        }

        calibrator = MovementCalibrator.FromProfile(profile, ClassNames, out error);
        if (calibrator == null)
        {
            calibrationError = $"the saved calibration for '{userId}' cannot be used: {error}";
            return false;
        }

        calibrationBackendId = backendId;
        return true;
    }

    public void DeleteSavedCalibration(string userId) => calibrationStore.Delete(userId);

    public string GetResultsSummary() => results.Summary();

    public static void DeleteSavedResults()
    {
        if (System.IO.Directory.Exists(ResultsFolder))
        {
            System.IO.Directory.Delete(ResultsFolder, recursive: true);
        }
    }

    private void Initialise()
    {
        var manager = GetComponent<DeviceManager>();
        if (manager == null)
        {
            throw new InvalidOperationException($"{nameof(NewFaceAuModel)} needs a {nameof(DeviceManager)} on the same GameObject");
        }

        windowSource = new FaceWindowSource(manager.Require(InputType.FaceAU), WindowFrames, FeaturesPerFrame);

        if (backend != null)
        {
            return;
        }

        if (faceModel != null)
        {
            UseSparseVr();
        }
        else
        {
            Debug.LogWarning($"{nameof(NewFaceAuModel)}: no face model is assigned. Assign one, or call UseJev or UseBackend before the first prediction.");
        }
    }

    private void Activate(IEmotionBackend next)
    {
        if (ReferenceEquals(next, backend))
        {
            return;
        }

        var previous = backend;
        backend = next;
        if (calibrationBackendId != next.Id)
        {
            ClearCalibration();
        }

        lastError = null;
        lastLoggedError = null;

        if (previous != null && !ReferenceEquals(previous, sparseVr))
        {
            previous.Dispose();
        }
    }

    private CalibrationProfile ReferenceProfile()
    {
        if (!useCalibration || calibrator == null || !calibrator.IsReady)
        {
            return null;
        }

        if (!ReferenceEquals(referenceSource, calibrator))
        {
            referenceProfile = calibrator.ExportProfile();
            referenceSource = calibrator;
        }

        return referenceProfile;
    }

    private float[] CalibrationScores(FacePrediction prediction)
    {
        return backend != null && backend.Id == prediction.BackendId
            ? backend.PrepareForCalibration(prediction.Scores)
            : null;
    }

    private async Awaitable<PredictionOutcome> PredictCore(float timeoutSeconds)
    {
        if (!(timeoutSeconds > 0f))
        {
            throw new ArgumentOutOfRangeException(nameof(timeoutSeconds), timeoutSeconds, "must be above 0");
        }

        if (!started || windowSource == null)
        {
            await Awaitable.NextFrameAsync();
            if (!started || windowSource == null)
            {
                return Fail(PredictionErrorCode.NotReady, "the predictor has not started; add it to an active GameObject first", null);
            }
        }

        var active = backend;

        if (active == null)
        {
            await Awaitable.NextFrameAsync();
            return Fail(PredictionErrorCode.NotConfigured,
                        "no backend is selected: assign a face model, or call UseJev or UseBackend", null);
        }

        if (busy)
        {
            await Awaitable.NextFrameAsync();
            return Fail(PredictionErrorCode.Busy, "another prediction is already running", active.Id);
        }

        busy = true;
        try
        {
            var window = await windowSource.AwaitWindowAsync(timeoutSeconds);
            if (window == null)
            {
                return Fail(PredictionErrorCode.NoTracking,
                            $"timed out after {timeoutSeconds:F1}s waiting for {WindowFrames} valid face frames; " +
                            "face tracking is probably unavailable", active.Id);
            }

            long generation = windowSource.Generation;

            BackendResult result;
            try
            {
                result = await active.ScoreAsync(new BackendRequest(window, ClassNames, ReferenceProfile()));
            }
            catch (Exception e)
            {
                return Fail(PredictionErrorCode.Internal, $"{e.GetType().Name}: {e.Message}", active.Id);
            }

            if (!result.Ok)
            {
                return result.Error.HasValue
                    ? Report(PredictionOutcome.Failure(result.Error.Value))
                    : Fail(PredictionErrorCode.InvalidResponse, "the backend returned neither scores nor an error", active.Id);
            }

            if (!ScoreValidation.TryValidate(result.Scores, ClassNames.Length, out string problem))
            {
                return Fail(PredictionErrorCode.InvalidResponse, problem, active.Id);
            }

            if (result.Raw != null && !ScoreValidation.TryValidate(result.Raw, ClassNames.Length, out problem))
            {
                return Fail(PredictionErrorCode.InvalidResponse, "raw " + problem, active.Id);
            }

            int best = ScoreValidation.Argmax(result.Scores);
            float confidence = (result.Raw ?? result.Scores)[best];
            var prediction = new FacePrediction((Emotion)best, confidence, result.Scores, window, generation,
                                                result.Raw, active.Id);
            return Report(PredictionOutcome.Success(prediction));
        }
        finally
        {
            busy = false;
        }
    }

    private PredictionOutcome Fail(PredictionErrorCode code, string message, string backendId)
    {
        return Report(PredictionOutcome.Failure(new PredictionError(code, message, backendId)));
    }

    private PredictionOutcome Report(PredictionOutcome outcome)
    {
        if (outcome.Ok)
        {
            lastError = null;
            lastLoggedError = null;
            return outcome;
        }

        var error = outcome.Error.Value;
        lastError = error;

        string text = error.ToString();
        if (text != lastLoggedError)
        {
            lastLoggedError = text;
            Debug.LogWarning($"{nameof(NewFaceAuModel)}: {text}");
        }

        try
        {
            PredictionFailed?.Invoke(error);
        }
        catch (Exception e)
        {
            Debug.LogWarning($"{nameof(NewFaceAuModel)}: a PredictionFailed handler threw ({e.GetType().Name}: {e.Message}).");
        }

        return outcome;
    }

    private void RecordResult(PredictionOutcome outcome)
    {
        if (!saveResults)
        {
            return;
        }

        if (!outcome.Ok)
        {
            results.RecordFailure(outcome.Error.Value.Code);
            return;
        }

        var prediction = outcome.Prediction;
        var calibrated = CalibratedEmotion(prediction);
        var emotion = calibrated ?? prediction.Emotion;
        results.Record(emotion, prediction.ScoreOf(emotion), calibrated.HasValue, prediction.Emotion);
    }
}
