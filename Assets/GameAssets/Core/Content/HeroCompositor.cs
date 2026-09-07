using System.Collections.Generic;

namespace RelicRun.Core.Content
{
    /// <summary>
    /// One pixel of a composed hero: a role, and the tones laid over it.
    /// </summary>
    /// <remarks>
    /// Most pixels are just a role — hair, the dark side of an outfit — and carry no tones. A
    /// shadow or a highlight does not replace what is beneath it but MODIFIES it, so a pixel can
    /// be "the outfit's dark side, one tone darker again", and the tones stack in the order they
    /// were laid on.
    ///
    /// The source names each of these with a synthetic character handed out as it goes. Those
    /// characters are an artefact of the order it happened to compose in, so they are not
    /// reproduced here: a pixel says what it MEANS instead.
    /// </remarks>
    public readonly struct ComposedPixel
    {
        /// <summary>The role key underneath, or <c>.</c> for nothing at all.</summary>
        public readonly char Role;

        /// <summary>Modifier keys laid over it, first applied first. Empty for most pixels.</summary>
        public readonly string Tones;

        public ComposedPixel(char role, string tones = "")
        {
            Role = role;
            Tones = tones ?? "";
        }

        public bool IsEmpty { get { return Role == HeroCompositor.Empty && Tones.Length == 0; } }

        public override string ToString()
        {
            return Tones.Length == 0 ? Role.ToString() : Role + "+" + Tones;
        }
    }

    /// <summary>One state, composed: its frames and how they are played.</summary>
    public sealed class ComposedHero
    {
        public int Size;
        public int Ms;
        public string Mode = "once";

        /// <summary>Each frame is <see cref="Size"/> squared pixels, row by row.</summary>
        public readonly List<ComposedPixel[]> Frames = new List<ComposedPixel[]>();

        public ComposedPixel At(int frame, int x, int y) { return Frames[frame][y * Size + x]; }
    }

    /// <summary>
    /// Stacking a delver into pixels.
    /// </summary>
    /// <remarks>
    /// This is why a hero cannot be drawn as twelve sprites on top of one another. Two of the
    /// role keys are not colours: a shadow pixel means "whatever is beneath this, one tone
    /// darker", so what it finally is depends on what was stamped before it. The outline is the
    /// same shape of problem — it is drawn around the finished silhouette, which nothing knows
    /// until the stack is complete. Neither can be baked into a sprite, so the composition
    /// happens at runtime and the result is a small grid of indices for a shader to colour.
    ///
    /// The backdrop is composited separately: it goes down first, everything else is stacked and
    /// outlined on its own, and the result is laid over it. That is what keeps the outline from
    /// running around the backdrop as well.
    /// </remarks>
    public static class HeroCompositor
    {
        /// <summary>Nothing drawn.</summary>
        public const char Empty = '.';

        /// <summary>The slot the backdrop is drawn in, which is composited apart from the rest.</summary>
        public const string Backdrop = "bg";

        /// <summary>
        /// Modifier keys, and how strongly each tints what is under it.
        /// </summary>
        /// <remarks>
        /// Three strengths of shadow and three of highlight. The letter says which family
        /// supplies the tint — Shadow or Highlight — and the number says how much of it.
        /// </remarks>
        public static readonly IReadOnlyDictionary<char, ToneMod> Mods = new Dictionary<char, ToneMod>
        {
            { 'U', new ToneMod('D', 0.15) },
            { 'D', new ToneMod('D', 0.3) },
            { 'u', new ToneMod('D', 0.45) },
            { 'l', new ToneMod('L', 0.15) },
            { 'L', new ToneMod('L', 0.3) },
            { 'J', new ToneMod('L', 0.45) },
        };

        public static bool IsMod(char key) { return Mods.ContainsKey(key); }

        /// <summary>Composes every frame of one state.</summary>
        public static ComposedHero Compose(HeroPack pack, IReadOnlyDictionary<string, string> worn,
            string state)
        {
            HeroStateDef def = pack.State(state);
            var hero = new ComposedHero { Size = pack.Size, Ms = def.Ms, Mode = def.Mode };

            // A part may be drawn with more frames than the state claims, and then it animates
            // fully rather than being cut short.
            int count = def.Frames;
            for (int i = 0; i < pack.Stack.Count; i++)
            {
                string slot = pack.Stack[i];
                string id = Worn(pack, worn, slot);
                if (id == null) continue;

                HeroPart part = pack.Part(slot, id);
                List<string[]> frames;
                if (part != null && part.Frames.TryGetValue(state, out frames) &&
                    frames.Count > count)
                {
                    count = frames.Count;
                }
            }

            for (int frame = 0; frame < count; frame++)
            {
                hero.Frames.Add(Frame(pack, worn, state, frame));
            }

            return hero;
        }

        private static ComposedPixel[] Frame(HeroPack pack, IReadOnlyDictionary<string, string> worn,
            string state, int frame)
        {
            ComposedPixel[] behind = Blank(pack.Size);
            string backdrop = Worn(pack, worn, Backdrop);
            if (backdrop != null) Stamp(behind, pack, pack.Frame(Backdrop, backdrop, state, frame));

            ComposedPixel[] front = Blank(pack.Size);
            for (int i = 0; i < pack.Stack.Count; i++)
            {
                string slot = pack.Stack[i];
                if (slot == Backdrop) continue;

                string id = slot == HeroPack.BaseSlot ? HeroPack.BaseSlot : Worn(pack, worn, slot);
                if (id == null) continue;

                Stamp(front, pack, pack.Frame(slot, id, state, frame));
            }

            Outline(front, pack.Size);

            for (int i = 0; i < front.Length; i++)
            {
                if (!front[i].IsEmpty) behind[i] = front[i];
            }

            return behind;
        }

        /// <summary>
        /// What a slot is wearing, or null for nothing.
        /// </summary>
        /// <remarks>
        /// The body is always worn. Everything else is exactly what the caller asked for — a
        /// slot they did not mention wears nothing, and the pack's own DEFAULTS are deliberately
        /// not consulted here. Defaults belong to whoever assembles an outfit: the Changing Room
        /// applies them when it dresses a delver, and a lobby rolling a rival's appearance does
        /// not. Applying them at composition time would put trousers on delvers who were
        /// specifically drawn without them.
        /// </remarks>
        private static string Worn(HeroPack pack, IReadOnlyDictionary<string, string> worn,
            string slot)
        {
            if (slot == HeroPack.BaseSlot) return HeroPack.BaseSlot;

            string id;
            if (worn == null || !worn.TryGetValue(slot, out id) || id == null) return null;

            return id == HeroPack.Nothing || id.Length == 0 ? null : id;
        }

        private static ComposedPixel[] Blank(int size)
        {
            var grid = new ComposedPixel[size * size];
            for (int i = 0; i < grid.Length; i++) grid[i] = new ComposedPixel(Empty);
            return grid;
        }

        /// <summary>
        /// Lays one layer over the grid. A modifier tones what is beneath rather than hiding it.
        /// </summary>
        /// <remarks>
        /// A modifier landing on nothing paints itself instead, and a modifier landing on
        /// another bare modifier replaces it — only a real pixel underneath gets toned. Both are
        /// the source's, and both matter: the first is what lets a shadow be drawn deliberately,
        /// and the second is what stops two shadows from compounding into a hole.
        /// </remarks>
        private static void Stamp(ComposedPixel[] grid, HeroPack pack, string[] layer)
        {
            if (layer == null) return;

            for (int y = 0; y < layer.Length && y < pack.Size; y++)
            {
                string row = layer[y];
                for (int x = 0; x < row.Length && x < pack.Size; x++)
                {
                    char ch = row[x];
                    if (ch == Empty || ch == ' ') continue;

                    int at = y * pack.Size + x;
                    ComposedPixel below = grid[at];

                    bool tones = IsMod(ch) && !below.IsEmpty &&
                                 !(below.Tones.Length == 0 && IsMod(below.Role));

                    grid[at] = tones
                        ? new ComposedPixel(below.Role, below.Tones + ch)
                        : new ComposedPixel(ch);
                }
            }
        }

        /// <summary>
        /// Draws the silhouette: every empty pixel touching a solid one becomes the outline.
        /// </summary>
        /// <remarks>
        /// Toned pixels and bare modifiers do not count as solid, so a shadow does not cast an
        /// outline of its own — otherwise every shaded fold would be traced in black.
        /// </remarks>
        private static void Outline(ComposedPixel[] grid, int size)
        {
            var outlined = new List<int>();

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    if (!grid[y * size + x].IsEmpty) continue;

                    if (Solid(grid, size, x, y - 1) || Solid(grid, size, x, y + 1) ||
                        Solid(grid, size, x - 1, y) || Solid(grid, size, x + 1, y))
                    {
                        outlined.Add(y * size + x);
                    }
                }
            }

            for (int i = 0; i < outlined.Count; i++)
            {
                grid[outlined[i]] = new ComposedPixel(HeroPalette.OutlineKey);
            }
        }

        private static bool Solid(ComposedPixel[] grid, int size, int x, int y)
        {
            if (x < 0 || y < 0 || x >= size || y >= size) return false;

            ComposedPixel pixel = grid[y * size + x];
            return !pixel.IsEmpty && pixel.Tones.Length == 0 && !IsMod(pixel.Role);
        }

        /// <summary>Resolves a composed pixel to a colour, tones and all.</summary>
        /// <remarks>
        /// The tones are applied in the order they were laid on, each one painted over the last
        /// as transparent paint — so a doubly shadowed pixel really is darker than a singly
        /// shadowed one.
        /// </remarks>
        public static Rgb Colour(ComposedPixel pixel, IReadOnlyDictionary<char, Rgb> palette)
        {
            Rgb colour;
            if (!palette.TryGetValue(pixel.Role, out colour)) colour = new Rgb(0, 0, 0);

            for (int i = 0; i < pixel.Tones.Length; i++)
            {
                ToneMod mod = Mods[pixel.Tones[i]];

                Rgb tint;
                if (!palette.TryGetValue(mod.Family, out tint))
                {
                    tint = mod.Family == 'D' ? new Rgb(0, 0, 0) : new Rgb(255, 255, 255);
                }

                colour = HeroColour.Over(colour, tint, mod.Alpha);
            }

            return colour;
        }
    }

    /// <summary>Which family a modifier tints with, and how strongly.</summary>
    public readonly struct ToneMod
    {
        /// <summary>The role key whose colour is painted over: Shadow or Highlight.</summary>
        public readonly char Family;

        public readonly double Alpha;

        public ToneMod(char family, double alpha)
        {
            Family = family;
            Alpha = alpha;
        }
    }
}
