namespace VcuSwimLab.Backends
{
    public interface ICancelableBackend : IEmotionBackend
    {
        void CancelPending();
    }
}
