using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Unity.InferenceEngine;
using UnityEngine;
using VcuSeCenter.Calibration;
using VcuSeCenter.VREmotionDetection;

public class PublicApiContract
{
    [Test]
    public void Every_member_the_game_and_samples_use_still_compiles_with_its_released_signature()
    {
        Action neverRun = () =>
        {
            NewFaceAuModel model = null;

            int frames = NewFaceAuModel.WindowFrames;
            int features = NewFaceAuModel.FeaturesPerFrame;
            float timeout = NewFaceAuModel.DefaultTimeoutSeconds;
            const string modelPath = NewFaceAuModel.DefaultModelPath;
            string modelName = model.FaceModelName;

            model.Start();
            Awaitable<Tensor<float>> raw = model.PredictRaw();
            Awaitable<FacePrediction?> detailed = model.PredictDetailed();
            detailed = model.PredictDetailed(1f, false);
            detailed = model.PredictDetailed(saveResult: false);
            detailed = model.PredictDetailed(timeoutSeconds: 2f);
            Awaitable<(Emotion, float)> predicted = model.Predict();

            model.UseCalibration = true;
            bool use = model.UseCalibration;
            bool calibrated = model.IsCalibrated;
            string error = model.CalibrationError;
            model.StartCalibration();
            model.ClearCalibration();
            string path = NewFaceAuModel.CalibrationPath("player");
            bool has = model.HasSavedCalibration("player");
            bool saved = model.SaveCalibration("player");
            bool loaded = model.LoadCalibration("player");
            model.DeleteSavedCalibration("player");
            Awaitable<bool> one = model.CalibrateEmotion(Emotion.Anger);
            one = model.CalibrateEmotion(Emotion.Anger, 1f, 20);
            bool finished = model.FinishCalibration();
            model.BeginCalibrationEmotion(Emotion.Anger);
            bool added = model.AddCalibrationSample(0, null);
            int[] counts = model.CalibrationSampleCounts();

            FacePrediction prediction = default;
            NearestCentroid? decision = model.CalibratedDecision(prediction);
            Emotion? calibratedEmotion = model.CalibratedEmotion(prediction);

            string summary = model.GetResultsSummary();
            string file = model.ResultsFilePath;
            string folder = NewFaceAuModel.ResultsFolder;
            NewFaceAuModel.DeleteSavedResults();

            Emotion emotion = prediction.Emotion;
            float confidence = prediction.Confidence;
            float[] scores = prediction.Scores;
            float[][] window = prediction.Window;
            long generation = prediction.Generation;

            DeviceManager devices = null;
            Device device = devices.Require(InputType.FaceAU);

            var ring = new RingBuffer<float[]>(5, () => new float[70]);
            bool full = ring.Full;
            long ringGeneration = ring.Generation;
            int capacity = ring.Capacity;
            ring.Clear();
            ring.Push(new float[70]);
            float[][] snapshot = ring.Snapshot();
            Tensor<float> fromRing = ring.ToTensor();
            Tensor<float> fromWindow = snapshot.ToTensor();
            ring.Listen(device);

            var calibrator = new MovementCalibrator(new[] { "a", "b" });
            calibrator.BeginEmotion(0);
            bool observed = calibrator.Observe(new float[2], new float[0][]);
            observed = calibrator.ObserveAt(1, new float[2], new float[0][]);
            bool isReady = calibrator.IsReady;
            string calibratorError = calibrator.Error;
            IReadOnlyList<string> names = calibrator.ClassNames;
            bool complete = calibrator.IsEmotionComplete(0);
            int[] kept = calibrator.KeptSampleCounts();
            bool done = calibrator.Finish();
            NearestCentroid? classified = calibrator.Classify(new float[2], new float[0][]);
            CalibrationProfile profile = calibrator.ExportProfile();
            MovementCalibrator restored = MovementCalibrator.FromProfile(profile, new[] { "a", "b" }, out string restoreError);
            calibrator.Reset();
            int perEmotion = MovementCalibrator.SamplesPerEmotion;
            int toCollect = MovementCalibrator.SamplesToCollectPerEmotion;
            string version = MovementCalibrator.ImplementationVersion;

            var rebuilt = new CalibrationProfile("v", new[] { "a" }, new double[1][], new double[1][]);
            string profileVersion = rebuilt.ImplementationVersion;
            string[] profileNames = rebuilt.ClassNames;
            double[][] scoreCentroids = rebuilt.ScoreCentroids;
            double[][] movementCentroids = rebuilt.MovementCentroids;

            var centroid = new NearestCentroid(1, 0.5, 0.25, 0.25);
            int classIndex = centroid.ClassIndex;
            double winner = centroid.WinnerSimilarity;
            double runnerUp = centroid.RunnerUpSimilarity;
            double margin = centroid.Margin;

            var personal = new PersonalCentroidBuilder(2);
            bool taken = personal.Observe(0, 1, new float[2]);
            int ticks = personal.TickCount(0);
            int[] allTicks = personal.TickCounts();
            bool built = personal.TryBuild(new[] { "a", "b" }, out double[][] centroids, out string buildError);
            bool scored = PersonalCentroidBuilder.IsScoredTick(1);

            var movement = new MovementCentroidBuilder(2);
            bool movementTaken = movement.Observe(0, 1, new float[0][]);
            int movementTicks = movement.TickCount(0);
            bool movementBuilt = movement.TryBuild(new[] { "a", "b" }, out double[][] movementOut, out string movementError);

            double[] log = CalibrationMath.LogScore(new float[2]);
            double[] mean = CalibrationMath.Mean(new List<double[]>());
            double[][] blend = CalibrationMath.Blend(new double[1][], new double[1][], 0.4);
            double[] normalized = CalibrationMath.Normalized(new double[2]);
            double dot = CalibrationMath.Dot(new double[2], new double[2]);
            double cosine = CalibrationMath.CosineSimilarity(new double[2], new double[2]);
            NearestCentroid nearest = CalibrationMath.Nearest(new double[2][], new double[2]);
            double[] feature = CalibrationMath.MovementFeature(new float[0][]);
            NearestCentroid sum = CalibrationMath.NearestOfSum(new double[2][], new double[2], new double[2][], new double[2]);
        };

        Assert.NotNull(neverRun);
    }

    [Test]
    public void Frozen_calibration_values_and_strings_are_unchanged()
    {
        Assert.AreEqual(1e-12, CalibrationMath.Epsilon);
        Assert.AreEqual(1e-12, CalibrationMath.NormFloor);
        Assert.AreEqual(0.40, CalibrationMath.FrozenHybridAlpha);
        Assert.AreEqual(63, CalibrationMath.FaceActionUnitCount);
        Assert.AreEqual("logscore", CalibrationMath.FeatureSpaceName);
        Assert.AreEqual("log(clip(score, epsilon))", CalibrationMath.TransformName);
        Assert.AreEqual("cosine", CalibrationMath.MetricName);
        Assert.AreEqual("mean over the window's frames of the first 63 action units (face; tongue excluded)",
                        CalibrationMath.MovementFeatureName);
        Assert.AreEqual("nearest class by (cosine to the personal log-score centroid) + "
                        + "(cosine to the personal movement centroid); winner, runner-up and margin are these sums",
                        CalibrationMath.MovementRuleName);
        Assert.AreEqual(1, PersonalCentroidBuilder.FirstScoredTick);
        Assert.AreEqual(9, PersonalCentroidBuilder.LastScoredTick);
        Assert.AreEqual(9, PersonalCentroidBuilder.RequiredTicksPerCentroid);
        Assert.AreEqual(9, MovementCalibrator.SamplesPerEmotion);
        Assert.AreEqual(10, MovementCalibrator.SamplesToCollectPerEmotion);
        Assert.AreEqual("runtime-calibration-v1", MovementCalibrator.ImplementationVersion);
    }

    [Test]
    public void Model_input_contract_and_enum_values_are_unchanged()
    {
        Assert.AreEqual(5, NewFaceAuModel.WindowFrames);
        Assert.AreEqual(70, NewFaceAuModel.FeaturesPerFrame);
        Assert.AreEqual(3f, NewFaceAuModel.DefaultTimeoutSeconds);
        // The bundled model was replaced on purpose under a new file name (it keeps the old model's GUID; a saved calibration
        // records the file name as the backend id, so calibrations made with the old model are refused). PackageContractTests
        // checks the file is there.
        Assert.AreEqual(
            "Packages/com.vcu-swim-lab.vr-emotion-detection/Runtime/Models/"
            + "sparseVR_teacher_model_acted_only_vol-aug-k1_v2_seed42.onnx",
            NewFaceAuModel.DefaultModelPath);

        CollectionAssert.AreEqual(
            new[] { "Anger", "Disgust", "Fear", "Happiness", "Neutral", "Sadness", "Surprise" },
            Enum.GetNames(typeof(Emotion)));
        CollectionAssert.AreEqual(new[] { 0, 1, 2, 3, 4, 5, 6 }, Enum.GetValues(typeof(Emotion)).Cast<int>().ToArray());
        CollectionAssert.AreEqual(new[] { "FaceAU", "Sound", "Count" }, Enum.GetNames(typeof(InputType)));
    }

    [Test]
    public void Results_csv_keeps_its_header_and_row_formats()
    {
        string folder = Path.Combine(Path.GetTempPath(), "contract-" + Guid.NewGuid().ToString("N"));
        Application.persistentDataPath = folder;
        var recorder = new ResultsRecorder();

        recorder.Record(Emotion.Happiness, 0.81234f, true, Emotion.Surprise);
        recorder.Record(Emotion.Neutral, 0.5f, false, Emotion.Neutral);
#if NEW_API
        recorder.RecordFailure(VcuSeCenter.Backends.PredictionErrorCode.NoTracking);
#else
        recorder.RecordNoTracking();
#endif
        recorder.Dispose();

        var lines = File.ReadAllLines(recorder.FilePath);
        Assert.AreEqual("time_utc,seconds,emotion,confidence,calibrated,model_emotion", lines[0]);
        StringAssert.EndsWith(",Happiness,0.8123,yes,Surprise", lines[1]);
        StringAssert.EndsWith(",Neutral,0.5000,no,Neutral", lines[2]);
        StringAssert.EndsWith(",no_tracking,,,", lines[3]);
        StringAssert.IsMatch(@"^\d{4}-\d\d-\d\dT\d\d:\d\d:\d\d\.\d{3}Z,\d+\.\d{3},", lines[1]);
        StringAssert.IsMatch(@"session_\d{8}_\d{6}\.csv$", recorder.FilePath);
        Assert.AreEqual(Path.Combine(folder, "VREmotionDetection", "results"), ResultsRecorder.Folder);
    }

    [Test]
    public void Calibration_file_location_and_id_rules_are_unchanged()
    {
        string folder = Path.Combine(Path.GetTempPath(), "contract-" + Guid.NewGuid().ToString("N"));
        Application.persistentDataPath = folder;

        Assert.AreEqual(Path.Combine(folder, "VREmotionDetection", "calibration", "player_1-A.json"),
                        NewFaceAuModel.CalibrationPath("player_1-A"));
        Assert.Throws<ArgumentException>(() => NewFaceAuModel.CalibrationPath("../escape"));
        Assert.Throws<ArgumentException>(() => NewFaceAuModel.CalibrationPath(""));
        Assert.Throws<ArgumentException>(() => NewFaceAuModel.CalibrationPath("a b"));
    }
}
