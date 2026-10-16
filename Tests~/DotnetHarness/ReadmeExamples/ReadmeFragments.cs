using System.Threading.Tasks;
using UnityEngine;
using VcuSwimLab.Backends.Jev;

public class ReadmeFragments : MonoBehaviour
{
    private NewFaceAuModel predictor;

    public async Task Hero()
    {
        var (emotion, confidence) = await predictor.Predict();
    }

    public void JevOptionsFragment(string apiKey)
    {
        var options = new JevOptions(apiKey);
        options.Model = "jev-1.13.0";
        options.TimeoutSeconds = 10;
        options.MaxRetries = 2;
        options.SendOwnFacesInRequest = false;
        predictor.UseJev(options);
    }

    public void CustomBackendFragment()
    {
        predictor.UseBackend(new AlwaysNeutralBackend());
    }
}
