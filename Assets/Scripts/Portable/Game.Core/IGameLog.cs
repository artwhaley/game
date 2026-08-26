namespace TruthCardGame.Core
{
    public interface IGameLog
    {
        void Info(string message);
        void Warning(string message);
        void Error(string message);
    }

    public sealed class NullGameLog : IGameLog
    {
        public void Info(string message) { }
        public void Warning(string message) { }
        public void Error(string message) { }
    }
}
