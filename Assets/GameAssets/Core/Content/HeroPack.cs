using System.Collections.Generic;

namespace RelicRun.Core.Content
{
    /// <summary>How long a state runs and what it does at the end.</summary>
    public sealed class HeroStateDef
    {
        /// <summary>Frames the state is meant to have. A part may draw more, and then it wins.</summary>
        public int Frames = 1;

        /// <summary>Milliseconds a frame is held.</summary>
        public int Ms = 100;

        /// <summary>"loop", "once" or "hold".</summary>
        public string Mode = "once";
    }

    /// <summary>One garment, or the body: what it looks like in each state.</summary>
    public sealed class HeroPart
    {
        public readonly string Slot;
        public readonly string Id;

        /// <summary>State name to its frames, each frame a grid of role keys, row by row.</summary>
        public readonly Dictionary<string, List<string[]>> Frames =
            new Dictionary<string, List<string[]>>();

        public HeroPart(string slot, string id)
        {
            Slot = slot;
            Id = id;
        }
    }

    /// <summary>
    /// Everything needed to draw a delver.
    /// </summary>
    /// <remarks>
    /// A hero is a stack of layers — a backdrop, a body, and the wardrobe slots — each drawn as
    /// a square grid whose characters are ROLE KEYS rather than colours. Nothing here holds a
    /// colour: what a delver looks like is this, plus a palette, and the two are kept apart so
    /// the Changing Room can change one without touching the other.
    ///
    /// Frames are normalised to the canvas as they are added, which is what the source's
    /// importer does, so nothing downstream has to wonder whether a grid is the right size.
    /// </remarks>
    public sealed class HeroPack
    {
        /// <summary>The canvas is square, and this is its side.</summary>
        public int Size = 32;

        /// <summary>
        /// Paint order, back to front, including <c>base</c> for the body itself.
        /// </summary>
        /// <remarks>
        /// The backdrop is a special case and is not painted with the rest: it goes down first
        /// and everything else is composited over it, so the outline never runs around it.
        /// </remarks>
        public readonly List<string> Stack = new List<string>();

        /// <summary>
        /// How long each state runs, which comes from the RIG rather than from the pack.
        /// </summary>
        /// <remarks>
        /// A pack declares its own states and they are used to validate it — a part claiming
        /// more frames than its state allows is malformed — but they are not what the game
        /// animates from. Seeded from <see cref="HeroRig"/>, and a loader has no business
        /// overwriting it.
        /// </remarks>
        public readonly Dictionary<string, HeroStateDef> States =
            new Dictionary<string, HeroStateDef>(HeroRig.States);

        /// <summary>What a slot wears when nothing has been chosen for it.</summary>
        public readonly Dictionary<string, string> Defaults = new Dictionary<string, string>();

        /// <summary>The colours this pack was authored with, keyed by a family's base role.</summary>
        public readonly Dictionary<char, string> FamilyColours = new Dictionary<char, string>();

        private readonly Dictionary<string, HeroPart> _parts = new Dictionary<string, HeroPart>();

        /// <summary>The name of the body's own layer, which is not a wardrobe slot.</summary>
        public const string BaseSlot = "base";

        /// <summary>What a slot wearing nothing is called.</summary>
        public const string Nothing = "none";

        /// <summary>Adds a part, normalising every frame to the canvas.</summary>
        public void Add(HeroPart part)
        {
            foreach (KeyValuePair<string, List<string[]>> state in part.Frames)
            {
                for (int i = 0; i < state.Value.Count; i++)
                {
                    state.Value[i] = Normalise(state.Value[i]);
                }
            }

            _parts[Key(part.Slot, part.Id)] = part;
        }

        public HeroPart Part(string slot, string id)
        {
            HeroPart part;
            return _parts.TryGetValue(Key(slot, id), out part) ? part : null;
        }

        /// <summary>Every part the pack holds, which is what a wardrobe screen offers.</summary>
        public IEnumerable<HeroPart> Parts { get { return _parts.Values; } }

        /// <summary>
        /// The frame a part shows for a state, holding the last one if it has run out.
        /// </summary>
        /// <remarks>
        /// The ladder is the source's: the state's own frames if it has any, then the part's
        /// static drawing, then nothing. A static part therefore rides every frame of every
        /// state until somebody draws it properly, which is what lets a pack be finished a
        /// garment at a time.
        /// </remarks>
        public string[] Frame(string slot, string id, string state, int index)
        {
            HeroPart part = Part(slot, id);
            if (part == null) return null;

            List<string[]> frames;
            if (part.Frames.TryGetValue(state, out frames) && frames.Count > 0)
            {
                return frames[index < frames.Count ? index : frames.Count - 1];
            }

            return part.Frames.TryGetValue("static", out frames) && frames.Count > 0
                ? frames[0]
                : null;
        }

        /// <summary>The state's definition, or the idle one if it is not a state this pack has.</summary>
        public HeroStateDef State(string state)
        {
            HeroStateDef def;
            if (States.TryGetValue(state, out def)) return def;
            return States.TryGetValue("idle", out def) ? def : new HeroStateDef();
        }

        /// <summary>Pads a short grid with empty and crops a long one, to exactly the canvas.</summary>
        public string[] Normalise(string[] rows)
        {
            var full = new string[Size];
            for (int y = 0; y < Size; y++)
            {
                string row = rows != null && y < rows.Length ? rows[y] : "";
                if (row.Length < Size) row = row.PadRight(Size, '.');
                full[y] = row.Length > Size ? row.Substring(0, Size) : row;
            }

            return full;
        }

        private static string Key(string slot, string id) { return slot + "/" + id; }
    }
}
