namespace VcuSwimLab.Backends
{
    public static class WindowValidation
    {
        public static bool TryValidate(float[][] window, int minFrames, int minFeatures, out string problem)
        {
            if (window == null)
            {
                problem = "no window was provided";
                return false;
            }

            if (window.Length < minFrames)
            {
                problem = $"the window has {window.Length} frames but at least {minFrames} are needed";
                return false;
            }

            for (int f = 0; f < window.Length; ++f)
            {
                if (window[f] == null || window[f].Length < minFeatures)
                {
                    problem = $"frame {f} has {(window[f] == null ? 0 : window[f].Length)} values but at least {minFeatures} are needed";
                    return false;
                }

                for (int a = 0; a < window[f].Length; ++a)
                {
                    if (float.IsNaN(window[f][a]) || float.IsInfinity(window[f][a]))
                    {
                        problem = $"frame {f} value {a} is not a finite number";
                        return false;
                    }
                }
            }

            problem = null;
            return true;
        }
    }
}
