using System;
using System.IO;
using System.Text.RegularExpressions;
using UnityEngine;
using VcuSeCenter.Calibration;

namespace VcuSeCenter.VREmotionDetection
{
    public sealed class CalibrationStore
    {
        private static readonly Regex UserIdPattern = new Regex(@"^[A-Za-z0-9_-]+\z");

        private readonly string rootFolder;

        public CalibrationStore(string rootFolder = null)
        {
            this.rootFolder = rootFolder;
        }

        public static string DefaultRoot => Path.Combine(Application.persistentDataPath, StoragePaths.Root, StoragePaths.Calibration);

        public static string DefaultPathFor(string userId) => PathIn(DefaultRoot, userId);

        public string PathFor(string userId) => PathIn(rootFolder ?? DefaultRoot, userId);

        public bool Exists(string userId) => File.Exists(PathFor(userId));

        public void Delete(string userId)
        {
            string path = PathFor(userId);
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }

        public bool TrySave(string userId, string modelId, CalibrationProfile profile, out string error)
        {
            if (profile == null)
            {
                throw new ArgumentNullException(nameof(profile));
            }

            string path = PathFor(userId);

            try
            {
                var saved = new SavedCalibration
                {
                    implementationVersion = profile.ImplementationVersion,
                    faceModel = modelId,
                    savedUtc = DateTime.UtcNow.ToString("o"),
                    classNames = profile.ClassNames,
                    scoreWidth = profile.ScoreCentroids[0].Length,
                    scoreCentroids = Flatten(profile.ScoreCentroids),
                    movementWidth = profile.MovementCentroids[0].Length,
                    movementCentroids = Flatten(profile.MovementCentroids),
                };

                Directory.CreateDirectory(Path.GetDirectoryName(path));
                string temporary = path + ".tmp";
                File.WriteAllText(temporary, JsonUtility.ToJson(saved));

                if (File.Exists(path))
                {
                    File.Replace(temporary, path, null);
                }
                else
                {
                    File.Move(temporary, path);
                }

                error = null;
                return true;
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                error = $"the calibration for '{userId}' could not be saved: {e.Message}";
                return false;
            }
        }

        public bool TryLoad(string userId, string modelId, out CalibrationProfile profile, out string error)
        {
            profile = null;
            string path = PathFor(userId);

            if (!File.Exists(path))
            {
                error = $"no saved calibration for '{userId}'";
                return false;
            }

            SavedCalibration saved;
            try
            {
                saved = JsonUtility.FromJson<SavedCalibration>(File.ReadAllText(path));
            }
            catch (Exception e)
            {
                error = $"the saved calibration for '{userId}' could not be read: {e.Message}";
                return false;
            }

            if (saved == null || saved.faceModel != modelId)
            {
                error = $"the saved calibration for '{userId}' was made with model " +
                        $"'{saved?.faceModel}', not '{modelId}'; calibrate again";
                return false;
            }

            profile = new CalibrationProfile(saved.implementationVersion, saved.classNames,
                                             Unflatten(saved.scoreCentroids, saved.scoreWidth),
                                             Unflatten(saved.movementCentroids, saved.movementWidth));
            error = null;
            return true;
        }

        private static string PathIn(string root, string userId)
        {
            if (string.IsNullOrEmpty(userId) || !UserIdPattern.IsMatch(userId))
            {
                throw new ArgumentException("userId may contain only letters, digits, '-' and '_'", nameof(userId));
            }

            return Path.Combine(root, userId + ".json");
        }

        private static double[] Flatten(double[][] matrix)
        {
            int width = matrix[0].Length;
            var flat = new double[matrix.Length * width];
            for (int r = 0; r < matrix.Length; ++r) Array.Copy(matrix[r], 0, flat, r * width, width);
            return flat;
        }

        private static double[][] Unflatten(double[] flat, int width)
        {
            if (flat == null || width <= 0 || flat.Length % width != 0) return null;

            var matrix = new double[flat.Length / width][];
            for (int r = 0; r < matrix.Length; ++r)
            {
                matrix[r] = new double[width];
                Array.Copy(flat, r * width, matrix[r], 0, width);
            }

            return matrix;
        }

        [Serializable]
        private class SavedCalibration
        {
            public string implementationVersion;
            public string faceModel;
            public string savedUtc;
            public string[] classNames;
            public int scoreWidth;
            public double[] scoreCentroids;
            public int movementWidth;
            public double[] movementCentroids;
        }
    }
}
