using System;
using System.Collections.Generic;
using System.Globalization;

namespace VcuSeCenter.Calibration
{
    public sealed class MovementCentroidBuilder
    {
        private readonly int classCount;
        private readonly List<double[]>[] observations;
        private string fault;

        public MovementCentroidBuilder(int classCount)
        {
            if (classCount <= 1)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(classCount), classCount, "need at least two classes");
            }

            this.classCount = classCount;
            observations = new List<double[]>[classCount];
            for (int c = 0; c < classCount; ++c)
            {
                observations[c] = new List<double[]>(PersonalCentroidBuilder.RequiredTicksPerCentroid);
            }
        }

        public int TickCount(int classIndex) => observations[classIndex].Count;

        public bool Observe(int classIndex, int tickIndex, IReadOnlyList<float[]> window)
        {
            if (classIndex < 0 || classIndex >= classCount)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(classIndex), classIndex, $"outside 0..{classCount - 1}");
            }

            if (!PersonalCentroidBuilder.IsScoredTick(tickIndex) || window == null)
            {
                return false;
            }

            try
            {
                observations[classIndex].Add(CalibrationMath.MovementFeature(window));
                return true;
            }
            catch (ArgumentException e)
            {
                Fault($"a calibration window for class {classIndex} was refused by the movement "
                      + $"feature: {e.Message}");
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
                if (have != PersonalCentroidBuilder.RequiredTicksPerCentroid)
                {
                    short_.Add($"{Name(classNames, c)} {have}/{PersonalCentroidBuilder.RequiredTicksPerCentroid}");
                }
            }

            if (short_.Count > 0)
            {
                error = "the calibration block did not contribute "
                        + $"{PersonalCentroidBuilder.RequiredTicksPerCentroid} tracked windows for "
                        + "every class: " + string.Join(", ", short_)
                        + ". Movement calibration needs the action-unit window of every scored "
                        + "calibration tick.";
                return false;
            }

            var fitted = new double[classCount][];
            for (int c = 0; c < classCount; ++c)
            {
                fitted[c] = CalibrationMath.Mean(observations[c]);
            }

            centroids = fitted;
            error = null;
            return true;
        }

        private static string Name(IReadOnlyList<string> classNames, int c)
            => classNames != null && c < classNames.Count
                ? classNames[c]
                : c.ToString(CultureInfo.InvariantCulture);

        private void Fault(string message)
        {
            if (fault == null) fault = message;
        }
    }
}
