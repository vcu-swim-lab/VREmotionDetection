using UnityEngine;
using VcuSeCenter.VREmotionDetection;

[RequireComponent(typeof(NewFaceAuModel))]
public class FirstPrediction : MonoBehaviour
{
    async void Start()
    {
        var predictor = GetComponent<NewFaceAuModel>();

        var (emotion, confidence) = await predictor.Predict();

        Debug.Log($"{emotion} ({confidence:F2})");
    }
}
