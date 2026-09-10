using System;
using RelicRun.Core.Content;
using RelicRun.Core.Presentation;
using RelicRun.Game.Services;
using UnityEngine;

namespace RelicRun.Game.Presentation
{
    /// <summary>
    /// One of the screens that is not a run.
    /// </summary>
    /// <remarks>
    /// The source keeps all of these in one tree and switches on a string; this keeps them as
    /// panels in one scene and switches on a <see cref="Page"/>. Same arrangement, and it is the
    /// right one: eleven screens loaded as eleven addressable prefabs would be eleven waits and
    /// eleven flickers, for screens that are a few hundred objects between them.
    ///
    /// A panel knows how to draw itself and where it would like to go. It does NOT know what is
    /// showing, who else exists, or how to get there — that belongs to <see cref="MetaScene"/>,
    /// and keeping it there is what stops eleven screens each holding a reference to the other
    /// ten.
    /// </remarks>
    public abstract class MetaPanel : MonoBehaviour
    {
        /// <summary>Which screen this panel is.</summary>
        /// <remarks>
        /// Named <c>Shows</c> rather than <c>Page</c>. A property may share its type's name and
        /// the compiler will resolve it, but every subclass would then be writing
        /// <c>Page.Levels</c> inside a member called <c>Page</c>, which is a sentence nobody
        /// should have to parse.
        /// </remarks>
        public abstract Page Shows { get; }

        /// <summary>
        /// Whether this panel wants redrawing every second.
        /// </summary>
        /// <remarks>
        /// Almost none of them do. The title does, because it counts today's Daily down, and a
        /// clock that only moved when something else happened would be a clock that had stopped.
        ///
        /// It is asked rather than assumed because <see cref="Draw"/> is not always cheap: the
        /// dungeon list builds a pack of foes to preview a hall's king, and doing that once a
        /// second to redraw numbers that cannot have changed is work nobody asked for.
        /// </remarks>
        public virtual bool Ticks
        {
            get { return false; }
        }

        /// <summary>Whether this panel is the one showing.</summary>
        /// <remarks>
        /// The whole object goes inactive, so a hidden screen costs nothing: no layout, no
        /// raycasts, and no buttons behind the visible screen quietly taking presses.
        /// </remarks>
        public bool Showing
        {
            get { return gameObject.activeSelf; }
            set { gameObject.SetActive(value); }
        }

        /// <summary>Raised when the delver has asked to be somewhere else.</summary>
        /// <remarks>
        /// A request rather than a move. The panel says where it would like to go and the scene
        /// decides — which is what lets a screen be reached from two places without knowing it
        /// has been, and what keeps the back button's answer in one piece of code.
        /// </remarks>
        public event Action<Page> Wants;

        /// <summary>
        /// What the game says, in the delver's language.
        /// </summary>
        /// <remarks>
        /// Handed down by the scene rather than fetched, because fetching it is asynchronous and
        /// a panel drawing itself is not. Never null: an unfetched one answers every key with the
        /// key, which is ugly on screen and immediately diagnosable — unlike a blank.
        ///
        /// Not every screen needs it. The title and the mode picker are written in literals the
        /// source never translated, and saying so is the point: a screen that used this WOULD be
        /// translated, and one that does not is making a claim about the source.
        /// </remarks>
        public Locale Words { get; set; }

        /// <summary>
        /// What opens the modals, for the screens that have a button for one.
        /// </summary>
        /// <remarks>
        /// Handed down like the words are. A panel that resolved its own would need the container,
        /// and a panel that knew about the container would be a panel that could reach anything.
        /// </remarks>
        public Modals Modals { get; set; }

        /// <summary>Puts the delver's current state on the screen.</summary>
        /// <param name="vault">Never null: the scene substitutes an empty save if it has none.</param>
        /// <param name="now">
        /// One instant for the whole draw. Read by the scene and passed down, so everything on a
        /// screen is about the same moment rather than about whenever each piece asked.
        /// </param>
        public abstract void Draw(SaveVault vault, DateTimeOffset now);

        /// <summary>Asks to go somewhere. For subclasses to call from their own buttons.</summary>
        protected void Go(Page page)
        {
            Action<Page> asked = Wants;

            if (asked != null) asked(page);
        }
    }
}
