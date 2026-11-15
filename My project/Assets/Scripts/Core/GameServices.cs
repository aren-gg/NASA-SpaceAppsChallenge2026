using MarsTrainer.Data;
using MarsTrainer.Sim;

namespace MarsTrainer.Core
{
    public static class GameServices
    {
        public static SimClock SimClock { get; set; }
        public static GameState GameState { get; set; }
        public static DataRepository DataRepository { get; set; }
    }
}
