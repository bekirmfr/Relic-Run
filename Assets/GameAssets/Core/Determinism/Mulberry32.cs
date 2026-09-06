namespace RelicRun.Core.Determinism
{
    /// <summary>
    /// Bit-exact port of the JS <c>mulberry32</c> PRNG that seeds every run.
    /// </summary>
    /// <remarks>
    /// The original:
    /// <code>
    /// function mulberry32(a) {
    ///   return function () {
    ///     a |= 0; a = (a + 0x6d2b79f5) | 0;
    ///     let x = Math.imul(a ^ (a >>> 15), 1 | a);
    ///     x = (x + Math.imul(x ^ (x >>> 7), 61 | x)) ^ x;
    ///     return ((x ^ (x >>> 14)) >>> 0) / 4294967296;
    ///   };
    /// }
    /// </code>
    /// Every operation there is 32-bit: <c>|0</c> truncates to int32, <c>&gt;&gt;&gt;</c> is an
    /// unsigned shift, and <c>Math.imul</c> is a wrapping int32 multiply. Doing the whole thing
    /// in <see cref="uint"/> under <c>unchecked</c> reproduces it exactly, because +, *, ^ and |
    /// give identical bit patterns whether the operands are read as signed or unsigned, and
    /// <c>uint &gt;&gt;</c> is precisely <c>&gt;&gt;&gt;</c>.
    ///
    /// Draw order is part of the contract: the engine consumes values in a fixed sequence, so
    /// reordering two conditions in a port changes outcomes for the same seed. Verified against
    /// <c>Tools/corpus/rng.json</c>.
    /// </remarks>
    public sealed class Mulberry32
    {
        private uint _state;

        public Mulberry32(uint seed)
        {
            _state = seed;
        }

        /// <summary>
        /// The generator's raw 32-bit output — the exact numerator of the next draw.
        /// </summary>
        /// <remarks>
        /// This, not <see cref="Next"/>, is the determinism contract. A draw is exactly
        /// <c>raw / 2^32</c>, so the integer is lossless, whereas a double written to text and
        /// read back can land one ULP away in a parser that is not correctly rounded — which is
        /// what Unity's JSON parser does, and it would fail the gate on a port that is in fact
        /// bit-perfect. Compare raw values.
        /// </remarks>
        public uint NextRaw()
        {
            unchecked
            {
                _state += 0x6d2b79f5u;
                uint x = (_state ^ (_state >> 15)) * (1u | _state);
                x = (x + ((x ^ (x >> 7)) * (61u | x))) ^ x;
                return x ^ (x >> 14);
            }
        }

        /// <summary>Next double in [0, 1), matching the JS generator draw for draw.</summary>
        /// <remarks>
        /// Dividing by 2^32 is exact in IEEE 754 — no rounding happens here, so this agrees
        /// with the JS value bit for bit on every runtime.
        /// </remarks>
        public double Next()
        {
            return NextRaw() / 4294967296.0;
        }

        /// <summary>Integer in [0, exclusiveMax), matching <c>Math.floor(rng() * n)</c>.</summary>
        public int NextInt(int exclusiveMax)
        {
            return (int)(Next() * exclusiveMax);
        }
    }
}
