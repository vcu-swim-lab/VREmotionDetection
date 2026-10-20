namespace VcuSeCenter.Backends
{
    public interface ICancelableBackend : IEmotionBackend
    {
        void CancelPending();
    }
}
