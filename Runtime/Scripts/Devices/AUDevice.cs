using UnityEngine;

public class AUDevice : Device
{
    public const int DefaultFrequencyHz = 10;

    private readonly OVRFaceExpressions faceExpressions;
    private readonly float pollInterval;

    public AUDevice(GameObject go)
        : this(go, DefaultFrequencyHz) { }

    internal AUDevice(GameObject go, int freq)
        : base(go)
    {
        faceExpressions = go.GetComponent<OVRFaceExpressions>();
        pollInterval = 1.0f / freq;

        if (faceExpressions == null)
        {
            Debug.LogWarning($"{nameof(AUDevice)}: no {nameof(OVRFaceExpressions)} component on '{go.name}', so no face data will arrive.");
        }
    }

    internal override (InputType ty, int width) InputSpec => (InputType.FaceAU, NewFaceAuModel.FeaturesPerFrame);

    internal override (float interval, bool valid) Poll(float[] data)
    {
        if (faceExpressions == null || !faceExpressions.ValidExpressions)
        {
            return (pollInterval, false);
        }

        faceExpressions.CopyTo(data);
        return (pollInterval, true);
    }
}
