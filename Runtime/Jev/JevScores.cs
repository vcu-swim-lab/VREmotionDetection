using System.Collections.Generic;
using System.Linq;

namespace VcuSwimLab.Jev
{
    public static class JevScores
    {
        public const float Floor = 0.01f;

        public static float[] ForCalibration(IReadOnlyList<float> scores)
        {
            if (scores == null)
            {
                return null;
            }

            double total = scores.Sum() + Floor * scores.Count;
            return scores.Select(score => (float)((score + Floor) / total)).ToArray();
        }
    }
}
