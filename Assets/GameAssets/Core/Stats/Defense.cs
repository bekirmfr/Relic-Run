using System;
using RelicRun.Core.Determinism;

namespace RelicRun.Core.Stats
{
    /// <summary>Which damage-reduction curve the engine is running.</summary>
    public enum DefenseModel
    {
        /// <summary>The live model: <c>damage x K/(K+DEF)</c>.</summary>
        Percentage = 0,

        /// <summary>Legacy subtractive model, kept only for the Balance Lab's comparison toggle.</summary>
        Flat = 1,
    }

    /// <summary>
    /// Damage reduction. Port of <c>applyDef</c>.
    /// </summary>
    /// <remarks>
    /// K = 20 makes 1 DEF worth roughly 1 ATK at endgame scale, and removes the all-or-nothing
    /// cliff the old subtractive model had. A blow always lands for at least 1.
    /// </remarks>
    public static class Defense
    {
        /// <summary>The percentage model's constant. 20 DEF halves incoming damage.</summary>
        public const int K = 20;

        public static int Apply(int damage, int defense, DefenseModel model = DefenseModel.Percentage)
        {
            if (defense < 0)
            {
                defense = 0;
            }

            if (model == DefenseModel.Flat)
            {
                return Math.Max(1, damage - defense);
            }

            return Math.Max(1, JsMath.RoundToInt(damage * (double)K / (K + defense)));
        }
    }
}
