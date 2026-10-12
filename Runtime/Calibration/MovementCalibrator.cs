using System;
using System.Collections.Generic;

namespace VcuSwimLab.Calibration
{
    /// <summary>
    /// Movement calibration for one player: learn how this player makes each emotion, then
    /// classify every later prediction against it.
    /// </summary>
    /// <remarks>
    /// The same method the game's protocol runs in its `movement` mode (`RuntimeCalibrator`),
    /// built from the same builders and the same decision, `CalibrationMath.NearestOfSum`:
    /// per emotion, the cosine of the model's log-scores to the player's own score centroid,
    /// plus the cosine of the face movements to the player's own movement centroid. The
    /// highest sum wins.
    ///
    /// Per emotion it keeps nine samples. The first sample after `BeginEmotion` is dropped,
    /// as tick 0 is in the protocol: it is taken while the player is still changing face. So
    /// a complete emotion takes ten samples, and the protocol takes one per second.
    ///
    /// A missing sample (no tracking) is not counted, so the caller can simply try again.
    /// Calibration either completes for every emotion or reports why not; it never runs on a
    /// partial set.
    /// </remarks>
    public sealed class MovementCalibrator
    {
        /// <summary>Samples kept per emotion.</summary>
        public const int SamplesPerEmotion = PersonalCentroidBuilder.RequiredTicksPerCentroid;

        /// <summary>Samples taken per emotion, including the dropped first one.</summary>
        public const int SamplesToCollectPerEmotion = PersonalCentroidBuilder.LastScoredTick + 1;

        private readonly string[] classNames;
        private PersonalCentroidBuilder scoreBuilder;
        private MovementCentroidBuilder movementBuilder;
        private int[] taken;
        private int current;

        private double[][] scoreCentroids;
        private double[][] movementCentroids;

        /// <summary>True once `Finish` built a complete calibration.</summary>
        public bool IsReady { get; private set; }

        /// <summary>Why the last `Finish` failed; empty when it did not.</summary>
        public string Error { get; private set; } = string.Empty;

        public IReadOnlyList<string> ClassNames => classNames;

        /// <param name="classNames">The model's classes, in output order.</param>
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

        /// <summary>Forget everything and start a new calibration.</summary>
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

        /// <summary>
        /// Start collecting one emotion. Call it when the player is asked to make that face.
        /// </summary>
        /// <exception cref="InvalidOperationException">
        /// The emotion already has samples, or calibration is finished. Call `Reset` to redo it.
        /// </exception>
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

        /// <summary>True when the emotion has all its samples.</summary>
        public bool IsEmotionComplete(int classIndex)
        {
            CheckClass(classIndex);
            return taken[classIndex] >= SamplesToCollectPerEmotion;
        }

        /// <summary>
        /// Offer one sample of the current emotion: the model's scores and the window they were
        /// computed from.
        /// </summary>
        /// <returns>
        /// True when the sample was counted. False when it was missing (call again), or when the
        /// emotion is already complete.
        /// </returns>
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

        /// <summary>
        /// Build the calibration from the collected samples.
        /// </summary>
        /// <returns>False, with `Error` set, when any emotion is incomplete or a sample was bad.</returns>
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

        /// <summary>
        /// The calibrated decision for one prediction, or null when calibration is not ready or
        /// the prediction cannot be scored (missing or malformed scores or window).
        /// </summary>
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
