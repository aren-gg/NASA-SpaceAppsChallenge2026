namespace MarsTrainer.Sim
{
    public interface ISimSystem
    {
        int Order { get; }
        void Step(GameState state, double dtSol);
    }
}
