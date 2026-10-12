namespace VcuSwimLab.Calibration
{
    /// <summary>
    /// One user's finished movement calibration, as plain data for saving and loading.
    /// </summary>
    /// <remarks>
    /// Per class: the mean log-score vector and the mean of the 63 face action units over the
    /// calibration samples. This describes one person's face, so it belongs on that person's
    /// device only -- never in the model asset, a build, or a repository.
    /// </remarks>
    public sealed class CalibrationProfile
    {
        public string ImplementationVersion { get; }
        public string[] ClassNames { get; }
        public double[][] ScoreCentroids { get; }
        public double[][] MovementCentroids { get; }

        public CalibrationProfile(string implementationVersion, string[] classNames,
                                  double[][] scoreCentroids, double[][] movementCentroids)
        {
            ImplementationVersion = implementationVersion;
            ClassNames = classNames;
            ScoreCentroids = scoreCentroids;
            MovementCentroids = movementCentroids;
        }
    }
}
