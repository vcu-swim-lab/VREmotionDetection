using System;
using System.Linq;
using VcuSwimLab.Calibration;

internal static class FaceAuNames
{
    internal static readonly string[] Movement = Of(typeof(OVRFaceExpressions.FaceExpression));

    internal static string[] Of(Type expressionEnum)
    {
        return Enum.GetValues(expressionEnum)
            .Cast<object>()
            .Select(member => (value: Convert.ToInt32(member), name: member.ToString()))
            .Where(member => member.value >= 0)
            .OrderBy(member => member.value)
            .Take(CalibrationMath.FaceActionUnitCount)
            .Select(member => member.name)
            .ToArray();
    }
}
