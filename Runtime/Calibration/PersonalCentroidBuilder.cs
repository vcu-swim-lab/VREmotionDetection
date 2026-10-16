using System;
using System.Collections.Generic;
using System.Globalization;

namespace VcuSwimLab.Calibration
{
    public sealed class PersonalCentroidBuilder
    {
        public const int FirstScoredTick = 1;

        public const int LastScoredTick = 9;

        public const int RequiredTicksPerCentroid = 9;

        private readonly int classCount;
        private readonly List<double[]>[] observations;
        private readonly int[] offered;
        private string fault;

        public PersonalCentroidBuilder(int classCount)
        {
            if (classCount <= 1)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(classCount), classCount, "need at least two classes");
            }

            this.classCount = classCount;
            observations = new List<double[]>[classCount];
            offered = new int[classCount];
            for (int c = 0; c < classCount; ++c)
            {
                observations[c] = new List<double[]>(RequiredTicksPerCentroid);
            }
        }

        public static bool IsScoredTick(int tickIndex)
            => tickIndex >= FirstScoredTick && tickIndex <= LastScoredTick;

        public int TickCount(int classIndex) => observations[classIndex].Count;

        public bool Observe(int classIndex, int tickIndex, IReadOnlyList<float> scores)
        {
            if (classIndex < 0 || classIndex >= classCount)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(classIndex), classIndex, $"outside 0..{classCount - 1}");
            }

            if (!IsScoredTick(tickIndex))
            {
                return false;
            }

            offered[classIndex]++;

            if (scores == null)
            {
                return false;
            }

            if (scores.Count != classCount)
            {
                Fault($"a calibration tick carried {scores.Count} scores but the schema has "
                      + $"{classCount} classes");
                return false;
            }

            try
            {
                observations[classIndex].Add(CalibrationMath.LogScore(scores));
                return true;
            }
            catch (ArgumentException e)
            {
                Fault($"a calibration tick for class {classIndex} held a value the log-score "
                      + $"transform refused: {e.Message}");
                return false;
            }
        }

        public bool TryBuild(IReadOnlyList<string> classNames, out double[][] centroids, out string error)
        {
            centroids = null;

            if (fault != null)
            {
                error = fault;
                return false;
            }

            var short_ = new List<string>();
            for (int c = 0; c < classCount; ++c)
            {
                int have = observations[c].Count;
                if (have != RequiredTicksPerCentroid)
                {
                    string name = classNames != null && c < classNames.Count
                        ? classNames[c]
                        : c.ToString(CultureInfo.InvariantCulture);
                    short_.Add($"{name} {have}/{RequiredTicksPerCentroid}");
                }
            }

            if (short_.Count > 0)
            {
                error = "the calibration block did not contribute "
                        + $"{RequiredTicksPerCentroid} tracked ticks for every class: "
                        + string.Join(", ", short_)
                        + ". Ticks 0 and any untracked tick are excluded by design, so this "
                        + "normally means face tracking dropped during the calibration block.";
                return false;
            }

            var fitted = new double[classCount][];
            for (int c = 0; c < classCount; ++c)
            {
                var centroid = CalibrationMath.Mean(observations[c]);

                for (int i = 0; i < centroid.Length; ++i)
                {
                    if (double.IsNaN(centroid[i]) || double.IsInfinity(centroid[i]))
                    {
                        string name = classNames != null && c < classNames.Count
                            ? classNames[c]
                            : c.ToString(CultureInfo.InvariantCulture);
                        error = $"the centroid for {name} came out non-finite at dimension {i}";
                        return false;
                    }
                }

                fitted[c] = centroid;
            }

            centroids = fitted;
            error = null;
            return true;
        }

        public int[] TickCounts()
        {
            var counts = new int[classCount];
            for (int c = 0; c < classCount; ++c)
            {
                counts[c] = observations[c].Count;
            }

            return counts;
        }

        private void Fault(string message)
        {
            if (fault == null) fault = message;
        }
    }
}
