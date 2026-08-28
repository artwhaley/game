namespace TruthCardGame.Core
{
    public interface IRandomSource
    {
        int NextInt(int minInclusive, int maxExclusive);

        /// <summary>Uniform float in [minInclusive, maxExclusive); weighted Card selection uses it.</summary>
        float NextFloat(float minInclusive, float maxExclusive);
    }
}
