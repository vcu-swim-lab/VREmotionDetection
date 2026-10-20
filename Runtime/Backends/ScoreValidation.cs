using System.Collections.Generic;

namespace VcuSeCenter.Backends
{
    public static class ScoreValidation
    {
        public static bool TryValidate(IReadOnlyList<float> scores, int expectedCount, out string problem)
        {
            if (scores == null)
            {
                problem = "no scores were returned";
                return false;
            }

            if (scores.Count != expectedCount)
            {
                problem = $"expected {expectedCount} scores but got {scores.Count}";
                return false;
            }

            for (int i = 0; i < scores.Count; ++i)
            {
                if (float.IsNaN(scores[i]) || float.IsInfinity(scores[i]))
                {
                    problem = $"score {i} is not a finite number";
                    return false;
                }

                if (scores[i] < 0f)
                {
                    problem = $"score {i} is negative";
                    return false;
                }
            }

            problem = null;
            return true;
        }

        public static int Argmax(IReadOnlyList<float> scores)
        {
            if (scores == null || scores.Count == 0)
            {
                return -1;
            }

            int best = 0;
            for (int i = 0; i < scores.Count; ++i)
            {
                if (float.IsNaN(scores[i]))
                {
                    return -1;
                }

                if (scores[i] > scores[best])
                {
                    best = i;
                }
            }

            return best;
        }
    }
}
