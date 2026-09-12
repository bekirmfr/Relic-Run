namespace RelicRun.Core.Presentation
{
    /// <summary>
    /// How long a fight takes to watch.
    /// </summary>
    /// <remarks>
    /// Everything else in Core decides what happens. This decides only how long the showing of
    /// it takes, which makes it the one part of the port a person is meant to sit with and
    /// change until it feels right. The numbers are the source's, so it starts where the game
    /// was rather than where somebody guessed.
    ///
    /// The interesting one is <see cref="PerTickMs"/>. A fight is an ATB clock, and every event
    /// carries the tick it happened on, so playback can follow the fight's OWN pace: two events
    /// on the same tick land together, and a long wait for a slow enemy is a long wait on screen
    /// too. That is why a flurry reads as a flurry rather than as a metronome.
    /// </remarks>
    public sealed class PacingRules
    {
        /// <summary>One beat, in milliseconds.</summary>
        public int StepMs = 1000;

        /// <summary>A beat in a long fight, where a second each would outstay its welcome.</summary>
        public int BusyStepMs = 700;

        /// <summary>How many events make a fight long.</summary>
        public int BusyAfterEvents = 34;

        /// <summary>Everything, for a delver who asked their system for less motion.</summary>
        public int ReducedStepMs = 40;

        /// <summary>The shortest a beat can be however far the speed control is pushed.</summary>
        public int FloorStepMs = 30;

        /// <summary>Milliseconds per tick of the fight's own clock.</summary>
        public int PerTickMs = 500;

        /// <summary>The same, in a long fight.</summary>
        public int BusyPerTickMs = 350;

        /// <summary>
        /// The longest a single wait may be, however far apart two ticks are.
        /// </summary>
        /// <remarks>
        /// A slow delver against a slow foe can leave twenty ticks between one blow and the
        /// next, which at half a second a tick would be ten seconds of nothing happening.
        /// </remarks>
        public int LongestWaitMs = 2600;

        /// <summary>The shortest gap, as hundredths of a beat.</summary>
        /// <remarks>
        /// Written as a percentage because the source multiplies by 0.45 and rounds, and a
        /// corpus of integers cannot hold 0.45. The rounding is JavaScript's — halves go up —
        /// and it matters: at the fastest speed on a short fight the beat is 250ms and this is
        /// 112.5, which JavaScript makes 113 and .NET, left alone, would make 112.
        /// </remarks>
        public int ShortestGapPercent = 45;

        /// <summary>What the speed control cycles through. The last wraps back to the first.</summary>
        public int[] SpeedSteps = { 1, 2, 4 };

        /// <summary>
        /// How long the walk down the hall to a new foe takes.
        /// </summary>
        /// <remarks>
        /// Flat, and deliberately not divided by the speed. This one is not a wait but an
        /// animation with a length of its own: shortening the timer without shortening the walk
        /// would cut a delver off mid-stride. It is skipped entirely under reduced motion, which
        /// is the only setting that turns it off.
        /// </remarks>
        public int WalkMs = 3350;

        /// <summary>
        /// How long the card a fight opens on is held before the first blow.
        /// </summary>
        /// <remarks>
        /// AFTER the walk, not during it. The source announces a foe entering twice — once to
        /// set off down the hall toward them, and once to put their card up when the walking has
        /// stopped — and running the two together would play the whole approach behind an opaque
        /// overlay. Which is what this port did first: three and a half seconds of hall sliding
        /// where nobody could see it.
        ///
        /// Flat, like the walk and for the same reason: it is a thing to read rather than a wait.
        /// It is skipped by whatever skips the walk.
        /// </remarks>
        public int IntroMs = 3000;

        /// <summary>
        /// How long the foe takes to fly from its card into the frame it fights from.
        /// </summary>
        /// <remarks>
        /// The half second the source spends carrying the picture from the middle of the screen
        /// down to the corner it will be fought in. It is the only thing joining the announcement
        /// to the fight — without it the card blinks out and a foe appears somewhere else, and a
        /// delver has to work out for themselves that they are the same creature.
        /// </remarks>
        public int FlyMs = 520;

        /// <summary>
        /// How long a beaten foe takes to go out.
        /// </summary>
        /// <remarks>
        /// A fifth of a second, which is the source's. Short enough to stay out of the way of the
        /// log and long enough to be seen, which is the whole job — a corpse that vanished on the
        /// frame it died would read as the foe never having been there.
        /// </remarks>
        public int DieMs = 220;

        /// <summary>And a third of a second for something that had a name.</summary>
        /// <remarks>
        /// The source gives a boss and the Hoard-King half again as long. It is not decoration:
        /// the thing a delver spent a floor on should take longer to stop existing than the
        /// guards did.
        /// </remarks>
        public int DieBossMs = 340;

        /// <summary>
        /// How long the hall itself takes to slide, which is SHORTER than the walk.
        /// </summary>
        /// <remarks>
        /// Two thousand six hundred against three thousand three hundred and fifty, and the gap
        /// is deliberate in the source: the hall arrives before the delver stops walking, so the
        /// next foe is standing in a settled room rather than sliding into place underneath it.
        /// A pan timed to the walk would finish exactly as the fight starts, which reads as the
        /// room still moving when the first blow lands.
        /// </remarks>
        public int PanMs = 2600;

        /// <summary>
        /// Whether pressing the speed control also shortens the tick-proportional waits.
        /// </summary>
        /// <remarks>
        /// The source divides BOTH the beat and the per-tick figure by the speed when a fight
        /// starts, and then the button recomputes only the beat. So a delver who presses the
        /// control mid-fight halves the gaps between events on the same tick and changes nothing
        /// at all about the long waits — which are most of what they were trying to skip.
        ///
        /// That reads as an oversight rather than a decision, so the shipped rules fix it and
        /// <see cref="AsRecorded"/> keeps it. The diff between the two IS the change.
        /// </remarks>
        public bool SpeedShortensTheTickWait = true;

        /// <summary>What the port ships.</summary>
        public static PacingRules Shipped()
        {
            return new PacingRules();
        }

        /// <summary>The source's own answers, including the one that looks like a slip.</summary>
        public static PacingRules AsRecorded()
        {
            return new PacingRules { SpeedShortensTheTickWait = false };
        }
    }
}
