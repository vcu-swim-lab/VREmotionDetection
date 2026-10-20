using System;
using System.Collections.Generic;

namespace VcuSeCenter.Calibration
{
    public sealed class MovementCalibrator
    {
        public const string ImplementationVersion = "runtime-calibration-v1";

        public const int SamplesPerEmotion = PersonalCentroidBuilder.RequiredTicksPerCentroid;

        public const int SamplesToCollectPerEmotion = PersonalCentroidBuilder.LastScoredTick + 1;

        private readonly string[] classNames;
        private PersonalCentroidBuilder scoreBuilder;
        private MovementCentroidBuilder movementBuilder;
        private int[] taken;
        private int current;

        private double[][] scoreCentroids;
        private double[][] movementCentroids;

        public bool IsReady { get; private set; }

        public string Error { get; private set; } = string.Empty;

        public IReadOnlyList<string> ClassNames => classNames;

        public MovementCalibrator(IReadOnlyList<string> classNames)
        {
            if (classNames == null) throw new ArgumentNullException(nameof(classNames));
            if (classNames.Count < 2)
            {
                throw new ArgumentException("calibration needs at least two classes", nameof(classNames));
            }

            this.classNames = new string[classNames.Count];
            for (int i = 0; i < classNames.Count; ++i) this.classNames[i] = classNames[i];

            Reset();
        }

        public void Reset()
        {
            scoreBuilder = new PersonalCentroidBuilder(classNames.Length);
            movementBuilder = new MovementCentroidBuilder(classNames.Length);
            taken = new int[classNames.Length];
            current = -1;
            scoreCentroids = null;
            movementCentroids = null;
            IsReady = false;
            Error = string.Empty;
        }

        public void BeginEmotion(int classIndex)
        {
            CheckClass(classIndex);

            if (IsReady)
            {
                throw new InvalidOperationException("calibration is finished; call Reset to start again");
            }

            if (taken[classIndex] > 0)
            {
                throw new InvalidOperationException(
                    $"{classNames[classIndex]} was already collected; call Reset to start again");
            }

            current = classIndex;
        }

        public bool IsEmotionComplete(int classIndex)
        {
            CheckClass(classIndex);
            return taken[classIndex] >= SamplesToCollectPerEmotion;
        }

        public bool Observe(IReadOnlyList<float> scores, IReadOnlyList<float[]> window)
        {
            if (current < 0)
            {
                throw new InvalidOperationException("call BeginEmotion before Observe");
            }

            if (scores == null || window == null || IsEmotionComplete(current))
            {
                return false;
            }

            int sample = taken[current];
            scoreBuilder.Observe(current, sample, scores);
            movementBuilder.Observe(current, sample, window);
            taken[current]++;
            return true;
        }

        public bool ObserveAt(int sampleIndex, IReadOnlyList<float> scores, IReadOnlyList<float[]> window)
        {
            if (current < 0)
            {
                throw new InvalidOperationException("call BeginEmotion before ObserveAt");
            }

            if (sampleIndex < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(sampleIndex), sampleIndex, "must be 0 or more");
            }

            taken[current] = Math.Max(taken[current], Math.Min(sampleIndex + 1, SamplesToCollectPerEmotion));

            if (scores == null || window == null)
            {
                return false;
            }

            bool keptScores = scoreBuilder.Observe(current, sampleIndex, scores);
            bool keptMovement = movementBuilder.Observe(current, sampleIndex, window);
            return keptScores && keptMovement;
        }

        public int[] KeptSampleCounts() => scoreBuilder.TickCounts();

        public bool Finish()
        {
            IsReady = false;

            if (!scoreBuilder.TryBuild(classNames, out var scores, out string error)
                || !movementBuilder.TryBuild(classNames, out var movements, out error))
            {
                Error = error;
                return false;
            }

            scoreCentroids = scores;
            movementCentroids = movements;
            Error = string.Empty;
            IsReady = true;
            return true;
        }

        public NearestCentroid? Classify(IReadOnlyList<float> scores, IReadOnlyList<float[]> window)
        {
            if (!IsReady || scores == null || window == null || scores.Count != classNames.Length)
            {
                return null;
            }

            try
            {
                return CalibrationMath.NearestOfSum(
                    scoreCentroids, CalibrationMath.LogScore(scores),
                    movementCentroids, CalibrationMath.MovementFeature(window));
            }
            catch (ArgumentException)
            {
                return null;
            }
        }

        public CalibrationProfile ExportProfile()
        {
            if (!IsReady) return null;

            return new CalibrationProfile(ImplementationVersion, classNames, Copy(scoreCentroids),
                                          Copy(movementCentroids));
        }

        public static MovementCalibrator FromProfile(CalibrationProfile profile,
                                                     IReadOnlyList<string> classNames, out string error)
        {
            error = null;
            if (profile == null) { error = "no profile"; return null; }

            if (profile.ImplementationVersion != ImplementationVersion)
            {
                error = $"the profile was made by {profile.ImplementationVersion}, this is {ImplementationVersion}";
                return null;
            }

            if (profile.ClassNames == null || profile.ClassNames.Length != classNames.Count)
            {
                error = "the profile has a different number of classes";
                return null;
            }

            for (int c = 0; c < classNames.Count; ++c)
            {
                if (profile.ClassNames[c] != classNames[c])
                {
                    error = $"class {c} is '{profile.ClassNames[c]}' in the profile but '{classNames[c]}' here";
                    return null;
                }
            }

            if (!Valid(profile.ScoreCentroids, classNames.Count, classNames.Count)
                || !Valid(profile.MovementCentroids, classNames.Count, CalibrationMath.FaceActionUnitCount))
            {
                error = "the profile's centroids have the wrong shape or are not finite";
                return null;
            }

            var calibrator = new MovementCalibrator(classNames)
            {
                scoreCentroids = Copy(profile.ScoreCentroids),
                movementCentroids = Copy(profile.MovementCentroids),
                IsReady = true,
            };
            return calibrator;
        }

        private static bool Valid(double[][] matrix, int rows, int width)
        {
            if (matrix == null || matrix.Length != rows) return false;
            foreach (var row in matrix)
            {
                if (row == null || row.Length != width) return false;
                foreach (double v in row)
                {
                    if (double.IsNaN(v) || double.IsInfinity(v)) return false;
                }
            }

            return true;
        }

        private static double[][] Copy(double[][] source)
        {
            var copy = new double[source.Length][];
            for (int i = 0; i < source.Length; ++i) copy[i] = (double[])source[i].Clone();
            return copy;
        }

        private void CheckClass(int classIndex)
        {
            if (classIndex < 0 || classIndex >= classNames.Length)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(classIndex), classIndex, $"outside 0..{classNames.Length - 1}");
            }
        }
    }
}
