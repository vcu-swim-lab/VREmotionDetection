using System;
using System.Collections.Generic;

namespace VcuSwimLab.Calibration
{
    public static class CalibrationMath
    {
        public const double Epsilon = 1e-12;

        public const double FrozenHybridAlpha = 0.40;

        public const double NormFloor = 1e-12;

        public const string FeatureSpaceName = "logscore";
        public const string TransformName = "log(clip(score, epsilon))";
        public const string MetricName = "cosine";

        public static double[] LogScore(IReadOnlyList<float> scores)
        {
            if (scores == null) throw new ArgumentNullException(nameof(scores));

            var result = new double[scores.Count];
            for (int i = 0; i < scores.Count; ++i)
            {
                double value = scores[i];
                if (double.IsNaN(value) || double.IsInfinity(value))
                {
                    throw new ArgumentException(
                        $"score {i} is {value}; the log-score transform needs finite model output",
                        nameof(scores));
                }

                result[i] = Math.Log(value < Epsilon ? Epsilon : value);
            }

            return result;
        }

        public static double[] Mean(IReadOnlyList<double[]> vectors)
        {
            if (vectors == null) throw new ArgumentNullException(nameof(vectors));
            if (vectors.Count == 0)
            {
                throw new ArgumentException("cannot average an empty set of vectors", nameof(vectors));
            }

            int width = vectors[0].Length;
            var sum = new double[width];

            for (int v = 0; v < vectors.Count; ++v)
            {
                if (vectors[v].Length != width)
                {
                    throw new ArgumentException(
                        $"vector {v} has length {vectors[v].Length}, expected {width}",
                        nameof(vectors));
                }

                for (int i = 0; i < width; ++i)
                {
                    sum[i] += vectors[v][i];
                }
            }

            for (int i = 0; i < width; ++i)
            {
                sum[i] /= vectors.Count;
            }

            return sum;
        }

        public static double[][] Blend(double[][] personal, double[][] population, double alpha)
        {
            if (personal == null) throw new ArgumentNullException(nameof(personal));
            if (population == null) throw new ArgumentNullException(nameof(population));
            if (personal.Length != population.Length)
            {
                throw new ArgumentException(
                    $"personal has {personal.Length} centroids, population has {population.Length}");
            }
            if (!(alpha >= 0.0 && alpha <= 1.0))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(alpha), alpha, "the hybrid weight must lie in [0, 1]");
            }

            var blended = new double[personal.Length][];
            for (int e = 0; e < personal.Length; ++e)
            {
                if (personal[e].Length != population[e].Length)
                {
                    throw new ArgumentException(
                        $"centroid {e}: personal is {personal[e].Length}-dimensional, "
                        + $"population is {population[e].Length}-dimensional");
                }

                var row = new double[personal[e].Length];
                for (int i = 0; i < row.Length; ++i)
                {
                    row[i] = alpha * personal[e][i] + (1.0 - alpha) * population[e][i];
                }

                blended[e] = row;
            }

            return blended;
        }

        public static double[] Normalized(double[] vector)
        {
            if (vector == null) throw new ArgumentNullException(nameof(vector));

            double sumSquares = 0.0;
            for (int i = 0; i < vector.Length; ++i)
            {
                sumSquares += vector[i] * vector[i];
            }

            double scale = Math.Sqrt(sumSquares) + NormFloor;

            var unit = new double[vector.Length];
            for (int i = 0; i < vector.Length; ++i)
            {
                unit[i] = vector[i] / scale;
            }

            return unit;
        }

        public static double Dot(double[] a, double[] b)
        {
            if (a == null) throw new ArgumentNullException(nameof(a));
            if (b == null) throw new ArgumentNullException(nameof(b));
            if (a.Length != b.Length)
            {
                throw new ArgumentException($"length {a.Length} against length {b.Length}");
            }

            double total = 0.0;
            for (int i = 0; i < a.Length; ++i)
            {
                total += a[i] * b[i];
            }

            return total;
        }

        public static double CosineSimilarity(double[] a, double[] b)
            => Dot(Normalized(a), Normalized(b));

        public static NearestCentroid Nearest(double[][] centroids, double[] feature)
        {
            if (centroids == null) throw new ArgumentNullException(nameof(centroids));
            if (feature == null) throw new ArgumentNullException(nameof(feature));
            if (centroids.Length < 2)
            {
                throw new ArgumentException(
                    $"nearest-centroid classification needs at least two centroids, got {centroids.Length}",
                    nameof(centroids));
            }

            var unitFeature = Normalized(feature);

            int best = -1;
            double bestSimilarity = double.NegativeInfinity;
            double runnerUp = double.NegativeInfinity;

            for (int e = 0; e < centroids.Length; ++e)
            {
                double similarity = Dot(unitFeature, Normalized(centroids[e]));

                if (similarity > bestSimilarity)
                {
                    runnerUp = bestSimilarity;
                    bestSimilarity = similarity;
                    best = e;
                }
                else if (similarity > runnerUp)
                {
                    runnerUp = similarity;
                }
            }

            return new NearestCentroid(best, bestSimilarity, runnerUp, bestSimilarity - runnerUp);
        }

        public const int FaceActionUnitCount = 63;

        public const string MovementFeatureName =
            "mean over the window's frames of the first 63 action units (face; tongue excluded)";
        public const string MovementRuleName =
            "nearest class by (cosine to the personal log-score centroid) + "
            + "(cosine to the personal movement centroid); winner, runner-up and margin are these sums";

        public static double[] MovementFeature(IReadOnlyList<float[]> window)
        {
            if (window == null) throw new ArgumentNullException(nameof(window));
            if (window.Count == 0)
            {
                throw new ArgumentException("the window holds no frames", nameof(window));
            }

            var sum = new double[FaceActionUnitCount];
            for (int f = 0; f < window.Count; ++f)
            {
                var frame = window[f];
                if (frame == null || frame.Length < FaceActionUnitCount)
                {
                    throw new ArgumentException(
                        $"frame {f} has {(frame == null ? 0 : frame.Length)} features; the movement "
                        + $"feature needs at least {FaceActionUnitCount}", nameof(window));
                }

                for (int a = 0; a < FaceActionUnitCount; ++a)
                {
                    double value = frame[a];
                    if (double.IsNaN(value) || double.IsInfinity(value))
                    {
                        throw new ArgumentException(
                            $"frame {f} action unit {a} is {value}", nameof(window));
                    }

                    sum[a] += value;
                }
            }

            for (int a = 0; a < FaceActionUnitCount; ++a)
            {
                sum[a] /= window.Count;
            }

            return sum;
        }

        public static NearestCentroid NearestOfSum(double[][] scoreCentroids, double[] scoreFeature,
                                                   double[][] movementCentroids, double[] movementFeature)
        {
            if (scoreCentroids == null) throw new ArgumentNullException(nameof(scoreCentroids));
            if (movementCentroids == null) throw new ArgumentNullException(nameof(movementCentroids));
            if (scoreFeature == null) throw new ArgumentNullException(nameof(scoreFeature));
            if (movementFeature == null) throw new ArgumentNullException(nameof(movementFeature));
            if (scoreCentroids.Length != movementCentroids.Length)
            {
                throw new ArgumentException(
                    $"{scoreCentroids.Length} score centroids against "
                    + $"{movementCentroids.Length} movement centroids");
            }
            if (scoreCentroids.Length < 2)
            {
                throw new ArgumentException(
                    $"nearest-centroid classification needs at least two centroids, got {scoreCentroids.Length}");
            }

            var unitScore = Normalized(scoreFeature);
            var unitMovement = Normalized(movementFeature);

            int best = -1;
            double bestTotal = double.NegativeInfinity;
            double runnerUp = double.NegativeInfinity;

            for (int e = 0; e < scoreCentroids.Length; ++e)
            {
                double total = Dot(unitScore, Normalized(scoreCentroids[e]))
                               + Dot(unitMovement, Normalized(movementCentroids[e]));

                if (total > bestTotal)
                {
                    runnerUp = bestTotal;
                    bestTotal = total;
                    best = e;
                }
                else if (total > runnerUp)
                {
                    runnerUp = total;
                }
            }

            return new NearestCentroid(best, bestTotal, runnerUp, bestTotal - runnerUp);
        }
    }

    public readonly struct NearestCentroid
    {
        public readonly int ClassIndex;
        public readonly double WinnerSimilarity;
        public readonly double RunnerUpSimilarity;
        public readonly double Margin;

        public NearestCentroid(int classIndex, double winnerSimilarity,
                               double runnerUpSimilarity, double margin)
        {
            ClassIndex = classIndex;
            WinnerSimilarity = winnerSimilarity;
            RunnerUpSimilarity = runnerUpSimilarity;
            Margin = margin;
        }
    }
}
