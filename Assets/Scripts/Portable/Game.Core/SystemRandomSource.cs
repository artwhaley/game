using System;

namespace TruthCardGame.Core
{
    public sealed class SystemRandomSource : IRandomSource
    {
        private readonly Random _random;

        public SystemRandomSource()
        {
            _random = new Random();
        }

        public SystemRandomSource(int seed)
        {
            _random = new Random(seed);
        }

        public int NextInt(int minInclusive, int maxExclusive)
        {
            return _random.Next(minInclusive, maxExclusive);
        }

        public float NextFloat(float minInclusive, float maxExclusive)
        {
            return minInclusive + (float)_random.NextDouble() * (maxExclusive - minInclusive);
        }
    }
}
