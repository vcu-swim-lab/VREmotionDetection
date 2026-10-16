namespace VcuSwimLab.Calibration
{
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
