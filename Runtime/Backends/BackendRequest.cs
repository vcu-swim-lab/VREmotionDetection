using System.Collections.Generic;
using VcuSwimLab.Calibration;

namespace VcuSwimLab.Backends
{
    public readonly struct BackendRequest
    {
        public BackendRequest(float[][] window, IReadOnlyList<string> classNames, CalibrationProfile referenceProfile = null)
        {
            Window = window;
            ClassNames = classNames;
            ReferenceProfile = referenceProfile;
        }

        public float[][] Window { get; }

        public IReadOnlyList<string> ClassNames { get; }

        public CalibrationProfile ReferenceProfile { get; }
    }
}
