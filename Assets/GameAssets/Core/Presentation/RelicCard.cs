using System.Collections.Generic;
using RelicRun.Core.Content;

namespace RelicRun.Core.Presentation
{
    /// <summary>Which of a relic card's labelled lines a row is.</summary>
    public enum RelicRowKind
    {
        /// <summary>What it does with no prompting.</summary>
        Passive = 0,

        /// <summary>What sets it off by itself.</summary>
        Trigger = 1,

        /// <summary>What it does when it goes off, for a relic that goes off by itself.</summary>
        Activation = 2,

        /// <summary>The same, for one that only ever goes off through a socket.</summary>
        Relayed = 3,

        /// <summary>The trigger fitted to this copy.</summary>
        SocketTrigger = 4,

        /// <summary>The emitter fitted to this copy.</summary>
        SocketEmitter = 5,
    }

    /// <summary>One labelled line of a relic card.</summary>
    public struct RelicRow
    {
        public RelicRowKind Kind;

        /// <summary>What it says. English — see <see cref="RelicCards"/>.</summary>
        public string Text;
    }

    /// <summary>
    /// What the caller knows about the copy being looked at.
    /// </summary>
    /// <remarks>
    /// All four are run facts, and all four are absent when the card is opened from the relic
    /// book — which is the only place it can be opened from until the run layer exists. Default
    /// is therefore a legitimate, complete answer and not a placeholder: a relic nobody owns,
    /// nobody has awakened, and nothing is fitted to.
    /// </remarks>
    public struct RelicHolding
    {
        /// <summary>How many copies are held. Nothing is shown below two.</summary>
        public int Owned;

        /// <summary>Whether THIS copy has been awakened.</summary>
        public bool Awakened;

        /// <summary>The trigger fitted to this copy, or None.</summary>
        public SocketTrigger Trigger;

        /// <summary>The emitter fitted to this copy, or None.</summary>
        public SocketEmitter Emitter;
    }

    /// <summary>Everything one relic's card shows.</summary>
    public struct RelicCard
    {
        public RelicId Relic;

        public RelicKind Family;

        /// <summary>The family's name as the chip prints it: upper case, English.</summary>
        public string FamilyName;

        /// <summary>The family's colour, as <c>#RRGGBB</c>.</summary>
        public string FamilyHex;

        /// <summary>
        /// What goes in front of the name: a star, a lozenge, or nothing.
        /// </summary>
        /// <remarks>
        /// A relic that is both awakened and socketed shows the star. That is the source's
        /// precedence and it is the right way round — awakening changes what a relic DOES and a
        /// socket changes when, so the bigger claim goes first.
        /// </remarks>
        public string Mark;

        /// <summary>The key its name is translated under, or null.</summary>
        public string NameKey;

        /// <summary>Its English name, shown when there is no key.</summary>
        public string Name;

        /// <summary>Its English description, or null when the description is translated.</summary>
        public string What;

        /// <summary>The key its description is translated under, or null.</summary>
        public string WhatKey;

        /// <summary>The line in quotes, quoted. English.</summary>
        public string Flavour;

        /// <summary>
        /// What awakening did, to go IN FRONT of the description — or null when it has not.
        /// </summary>
        /// <remarks>
        /// Given separately rather than joined on, because the description may be a KEY and Core
        /// does not translate. The screen holds both halves and puts them together in whatever
        /// language it is reading.
        /// </remarks>
        public string Awoke;

        /// <summary>What the socket adds, to go after the description — or null.</summary>
        public string SocketNote;

        /// <summary>
        /// What awakening WOULD do, or null.
        /// </summary>
        /// <remarks>
        /// A promise, so it is shown only while it is still one: an already-awakened relic says
        /// what it did in <see cref="Awoke"/> instead, and a relic that cannot awaken says
        /// nothing. Eleven of the fifty cannot.
        /// </remarks>
        public string AwakeLine;

        public IReadOnlyList<RelicRow> Rows;

        /// <summary>The two channels it turns one into the other, or null.</summary>
        public Channel[] Wheel;

        /// <summary>Which modes it can appear in.</summary>
        public GameModes Modes;

        /// <summary>How many copies are held.</summary>
        public int Owned;

        /// <summary>Whether to say so, which is only worth doing past one.</summary>
        public bool ShowsOwned;
    }

    /// <summary>
    /// One relic's card, worked out.
    /// </summary>
    /// <remarks>
    /// The card a delver opens to decide whether a relic is worth taking, so it is the one
    /// screen in the game where being wrong costs a run. Everything structural comes from the
    /// generated tables rather than from anything typed here.
    ///
    /// The prose is English in all eight languages. The name and the description are the
    /// exception — nineteen relics have both translated, and this carries the key rather than
    /// the word so the screen can ask. Everything else on the card, the flavour line and the
    /// three rule rows and the awakening promise, is written into the source as literals that
    /// never reach its translation function, and no amount of care here changes that.
    ///
    /// What IS decided here is the assembly: which rows exist, what they are called, and which
    /// of the two mutually exclusive awakening lines is showing.
    /// </remarks>
    public static class RelicCards
    {
        /// <summary>
        /// The mark in front of an awakened relic's name.
        /// </summary>
        /// <remarks>
        /// A star, not the source's ✦. That character is in a range this game deliberately does
        /// not print: <see cref="Locale.Clean"/> strips U+2500–U+27BF out of every translated
        /// string, because the pixel face has none of it — which is why the supporter pack's own
        /// title arrives as "Supporter Pack" with the source's star already gone.
        ///
        /// It DID draw, through whatever the operating system had lying around, and that is the
        /// problem rather than the reprieve: the same character was being removed on one path
        /// and borrowed on another, and the borrowed one would come out as an empty box on the
        /// first device without it. So these marks are characters the bundled face actually has,
        /// and the modal now owes nothing to a font nobody shipped.
        /// </remarks>
        public const string AwakenedMark = "* ";

        /// <summary>The mark in front of a socketed one's — something is attached to it.</summary>
        public const string SocketedMark = "+ ";

        /// <summary>What an already-awakened relic says before its description.</summary>
        public const string AwokePrefix = "* AWAKENED — ";

        /// <summary>What one that could awaken says instead.</summary>
        public const string PromisePrefix = "* AWAKENED: ";

        /// <summary>
        /// What the footer calls each mode.
        /// </summary>
        /// <remarks>
        /// The source writes these with a pickaxe and crossed swords in front. Those two are
        /// dropped, and they are the only thing on any of these cards that is: the source runs
        /// in a browser, where an emoji font is always somewhere in the chain, and this build
        /// bakes its own face. Neither the pixel face nor either borrowed one has U+26CF or
        /// U+2694, so both came out as empty boxes and said so in the log.
        ///
        /// Everything else on these cards was moved the same way, for the same reason — see
        /// <see cref="AwakenedMark"/>.
        /// </remarks>
        public const string DelveWord = "DELVE";

        /// <summary>And the other one.</summary>
        public const string VersusWord = "VERSUS";

        /// <summary>The card for a relic nobody is holding — the relic book's case.</summary>
        public static RelicCard Of(RelicId relic)
        {
            return Of(relic, new RelicHolding());
        }

        /// <summary>The card for a relic, and for what is known about the copy held.</summary>
        public static RelicCard Of(RelicId relic, RelicHolding held)
        {
            RelicDef def = RelicCatalog.Get(relic);
            RelicTextDef text = RelicText.Get(relic);
            RelicLoreDef lore = RelicLore.Get(relic);
            SetDef family = SetCatalog.Get(def.Kind);

            SocketTextDef socket = Fitted(held);

            return new RelicCard
            {
                Relic = relic,
                Family = def.Kind,
                FamilyName = family != null ? family.Name : string.Empty,
                FamilyHex = family != null ? family.Hex : null,

                Mark = held.Awakened ? AwakenedMark : socket != null ? SocketedMark : string.Empty,

                NameKey = text != null ? text.NameKey : null,
                Name = text != null ? text.Name : string.Empty,
                What = text != null ? text.What : null,
                WhatKey = text != null ? text.WhatKey : null,

                Flavour = Quoted(lore != null ? lore.Flavour : null),

                Awoke = held.Awakened && lore != null && lore.Awake != null
                    ? AwokePrefix + lore.Awake
                    : null,

                AwakeLine = !held.Awakened && lore != null && lore.Awake != null
                    ? PromisePrefix + lore.Awake
                    : null,

                SocketNote = socket != null ? " — " + socket.Name + ": " + socket.What : null,

                Rows = Lines(lore, held, socket),

                Wheel = Wheel(def),
                Modes = def.Modes,

                Owned = held.Owned,
                ShowsOwned = held.Owned > 1,
            };
        }

        /// <summary>What a row is labelled.</summary>
        /// <remarks>
        /// English, and here rather than in the screen so that a row and its label cannot drift
        /// apart. The arrow on <see cref="RelicRowKind.Relayed"/> is the source's, and it earns
        /// its place: that row and <see cref="RelicRowKind.Activation"/> carry the SAME text and
        /// mean different things, because a relic with no trigger of its own can only ever fire
        /// through somebody else's socket.
        /// </remarks>
        public static string Label(RelicRowKind kind)
        {
            switch (kind)
            {
                case RelicRowKind.Passive: return "PASSIVE";
                case RelicRowKind.Trigger: return "TRIGGER";
                case RelicRowKind.Activation: return "ON ACTIVATION";
                case RelicRowKind.Relayed: return "› IF RELAYED";
                case RelicRowKind.SocketTrigger: return "+ SOCKETED TRIGGER";
                case RelicRowKind.SocketEmitter: return "+ SOCKETED EMITTER";
                default: return string.Empty;
            }
        }

        /// <summary>What colour that label is drawn in, as <c>#RRGGBB</c>.</summary>
        public static string Hex(RelicRowKind kind)
        {
            switch (kind)
            {
                case RelicRowKind.Passive: return "#8FA8C4";
                case RelicRowKind.Trigger: return "#C4906A";
                case RelicRowKind.Activation: return "#7C9A6A";
                case RelicRowKind.Relayed: return "#7C9A6A";
                default: return "#E3B341";
            }
        }

        /// <summary>The rows this relic has, in the order the card lists them.</summary>
        private static IReadOnlyList<RelicRow> Lines(RelicLoreDef lore, RelicHolding held,
            SocketTextDef socket)
        {
            var rows = new List<RelicRow>(4);

            if (lore == null) return rows;

            if (lore.Passive != null)
            {
                rows.Add(new RelicRow { Kind = RelicRowKind.Passive, Text = lore.Passive });
            }

            if (lore.Native != null)
            {
                rows.Add(new RelicRow { Kind = RelicRowKind.Trigger, Text = lore.Native });
            }

            if (lore.Activation != null)
            {
                rows.Add(new RelicRow
                {
                    Kind = lore.Native != null ? RelicRowKind.Activation : RelicRowKind.Relayed,
                    Text = lore.Activation,
                });
            }

            if (socket != null)
            {
                rows.Add(new RelicRow
                {
                    Kind = held.Trigger != SocketTrigger.None
                        ? RelicRowKind.SocketTrigger
                        : RelicRowKind.SocketEmitter,
                    Text = socket.Name + " — " + socket.What,
                });
            }

            return rows;
        }

        /// <summary>
        /// What is fitted to this copy, or null.
        /// </summary>
        /// <remarks>
        /// A copy carries one component, never both, so the trigger is asked about first and the
        /// emitter only if there is none. A holding with both set is malformed rather than
        /// meaningful, and answering with the trigger is a definite answer to a question that
        /// should not have been asked.
        /// </remarks>
        private static SocketTextDef Fitted(RelicHolding held)
        {
            if (held.Trigger != SocketTrigger.None) return SocketText.Of(held.Trigger);
            if (held.Emitter != SocketEmitter.None) return SocketText.Of(held.Emitter);

            return null;
        }

        /// <summary>
        /// What a relic turns into what, or null when it turns nothing.
        /// </summary>
        /// <remarks>
        /// A wheel needs both halves, exactly as in the book. A relic that reacts to something
        /// and emits nothing is not a wheel, and drawing half an arrow for it would claim the
        /// opposite of what is true.
        /// </remarks>
        private static Channel[] Wheel(RelicDef relic)
        {
            if (relic.Reacts == null || relic.Reacts.Length == 0) return null;
            if (relic.Emits == null || relic.Emits.Length == 0) return null;

            return new[] { relic.Reacts[0], relic.Emits[0] };
        }

        /// <summary>
        /// The flavour line, in the source's own curly quotes.
        /// </summary>
        /// <remarks>
        /// Quoted here rather than by the screen because a missing line must come back as
        /// nothing rather than as an empty pair of quotes, and that is a decision about the
        /// content rather than about the layout.
        /// </remarks>
        private static string Quoted(string line)
        {
            if (string.IsNullOrEmpty(line)) return null;

            return "“" + line + "”";
        }
    }
}
