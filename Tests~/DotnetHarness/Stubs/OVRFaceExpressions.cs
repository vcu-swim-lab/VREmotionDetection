using System;
using UnityEngine;

public class OVRFaceExpressions : MonoBehaviour
{
    public enum FaceExpression
    {
        Invalid = -1,
        BrowLowererL = 0,
        BrowLowererR = 1,
        CheekPuffL = 2,
        CheekPuffR = 3,
        CheekRaiserL = 4,
        CheekRaiserR = 5,
        CheekSuckL = 6,
        CheekSuckR = 7,
        ChinRaiserB = 8,
        ChinRaiserT = 9,
        DimplerL = 10,
        DimplerR = 11,
        EyesClosedL = 12,
        EyesClosedR = 13,
        EyesLookDownL = 14,
        EyesLookDownR = 15,
        EyesLookLeftL = 16,
        EyesLookLeftR = 17,
        EyesLookRightL = 18,
        EyesLookRightR = 19,
        EyesLookUpL = 20,
        EyesLookUpR = 21,
        InnerBrowRaiserL = 22,
        InnerBrowRaiserR = 23,
        JawDrop = 24,
        JawSidewaysLeft = 25,
        JawSidewaysRight = 26,
        JawThrust = 27,
        LidTightenerL = 28,
        LidTightenerR = 29,
        LipCornerDepressorL = 30,
        LipCornerDepressorR = 31,
        LipCornerPullerL = 32,
        LipCornerPullerR = 33,
        LipFunnelerLB = 34,
        LipFunnelerLT = 35,
        LipFunnelerRB = 36,
        LipFunnelerRT = 37,
        LipPressorL = 38,
        LipPressorR = 39,
        LipPuckerL = 40,
        LipPuckerR = 41,
        LipStretcherL = 42,
        LipStretcherR = 43,
        LipSuckLB = 44,
        LipSuckLT = 45,
        LipSuckRB = 46,
        LipSuckRT = 47,
        LipTightenerL = 48,
        LipTightenerR = 49,
        LipsToward = 50,
        LowerLipDepressorL = 51,
        LowerLipDepressorR = 52,
        MouthLeft = 53,
        MouthRight = 54,
        NoseWrinklerL = 55,
        NoseWrinklerR = 56,
        OuterBrowRaiserL = 57,
        OuterBrowRaiserR = 58,
        UpperLidRaiserL = 59,
        UpperLidRaiserR = 60,
        UpperLipRaiserL = 61,
        UpperLipRaiserR = 62,
        TongueTipInterdental = 63,
        TongueTipAlveolar = 64,
        TongueFrontDorsalPalate = 65,
        TongueMidDorsalPalate = 66,
        TongueBackDorsalVelar = 67,
        TongueOut = 68,
        TongueRetreat = 69,
        Max = 70,
    }

    public bool ValidExpressions { get; set; }
    public float[] Values = new float[70];

    public void CopyTo(float[] data)
    {
        Array.Copy(Values, data, Math.Min(Values.Length, data.Length));
    }
}
