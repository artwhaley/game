namespace TruthCardGame.Core
{
    public interface IRandomSource
    {
        int NextInt(int minInclusive, int maxExclusive);
    }
}
