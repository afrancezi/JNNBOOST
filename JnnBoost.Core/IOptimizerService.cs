namespace JnnBoost.Core
{
    public interface IOptimizerService
    {
        void FPSBoost();
        void GPUBoost();
        void OptimizeRAM();
        void CleanTemp();
        void CleanNetwork();
    }
}