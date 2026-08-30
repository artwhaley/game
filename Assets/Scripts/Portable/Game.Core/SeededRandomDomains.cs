using System;

namespace TruthCardGame.Core
{
    /// <summary>
    /// Derives stable, independent RNG streams from one visible run seed.
    /// Session selection never consumes the PhaseRun/Card stream, and the
    /// fixed domain salts avoid relying on runtime-dependent string hashes.
    /// </summary>
    public static class SeededRandomDomains
    {
        private const uint SessionSelectionSalt = 0x51E55101u;
        private const uint PhaseRunCardSalt = 0xCA4D5EEDu;
        private const uint DialogSelectionSalt = 0xD1A106u;

        public static IRandomSource CreateSessionSelection(int seed)
        {
            return new SystemRandomSource(DeriveSeed(seed, SessionSelectionSalt));
        }

        /// <summary>
        /// Dedicated Dialog From Tags stream. Fixed salt keeps it independent
        /// from Session selection and the PhaseRun/Card streams: adding or
        /// removing dialog actions never perturbs card draw randomness.
        /// </summary>
        public static IRandomSource CreateDialogSelection(int seed)
        {
            return new SystemRandomSource(DeriveSeed(seed, DialogSelectionSalt));
        }

        public static PhaseRunRngFactory CreatePhaseRunFactory(int seed)
        {
            return new PhaseRunRngFactory(DeriveSeed(seed, PhaseRunCardSalt));
        }

        public static int DeriveSeed(int seed, uint domainSalt)
        {
            unchecked
            {
                var value = (uint)seed ^ domainSalt;
                value += 0x9E3779B9u;
                value = (value ^ (value >> 16)) * 0x85EBCA6Bu;
                value = (value ^ (value >> 13)) * 0xC2B2AE35u;
                value ^= value >> 16;
                return (int)value;
            }
        }
    }
}
