using GameLift.Save;
using RelicRun.Core.Meta;
using UnityEngine;

namespace RelicRun.Game.Services
{
    /// <summary>
    /// A delver's progression, in the shape the save service wants it.
    /// </summary>
    /// <remarks>
    /// A thin coat over <see cref="SaveCodec"/> and nothing else. Everything that could be got
    /// wrong — which field goes where, what a torn line costs, whether a name with a newline in
    /// it survives — is in Core, where <c>dotnet test</c> can reach it. What is left here is the
    /// package's interface, which cannot be.
    /// </remarks>
    public sealed class Progress : ISaveable
    {
        public SaveState State = new SaveState();

        /// <summary>Lines the last read could not understand. Nonzero means something was lost.</summary>
        public int Damaged;

        /// <summary>The version that wrote the save, or zero for one that did not say.</summary>
        public int Version;

        public string Serialize()
        {
            return SaveCodec.Write(State);
        }

        /// <summary>
        /// A save, read back.
        /// </summary>
        /// <remarks>
        /// The cast is the package's shape rather than a choice: <c>ISaveable</c> declares this
        /// generic, and a repository only ever calls it with the type it holds. Passing anything
        /// else is a programming error and throws like one.
        /// </remarks>
        public T Deserialize<T>(string data) where T : ISaveable, new()
        {
            Loaded loaded = SaveCodec.Read(data);

            var progress = new Progress
            {
                State = loaded.Save,
                Damaged = loaded.Damaged,
                Version = loaded.Version,
            };

            return (T)(object)progress;
        }
    }

    /// <summary>What a delver chose, in the shape the save service wants it.</summary>
    /// <remarks>
    /// Kept in its own file on disk rather than alongside the progression, which is the point of
    /// the split: wiping a profile should not take a delver's name and language with it, and a
    /// torn progression file should not cost them their settings.
    /// </remarks>
    public sealed class Choices : ISaveable
    {
        public Preferences Chosen = new Preferences();

        public string Serialize()
        {
            return PreferenceCodec.Write(Chosen);
        }

        public T Deserialize<T>(string data) where T : ISaveable, new()
        {
            return (T)(object)new Choices { Chosen = PreferenceCodec.Read(data) };
        }
    }

    /// <summary>
    /// The one place the game's save is read from and written to.
    /// </summary>
    /// <remarks>
    /// Two files, one door. They are separate on disk and together here because every screen that
    /// wants one usually wants the other — the title shows a name and a level — and because two
    /// stores would be two caches with two opinions about which is current.
    ///
    /// The source writes on every change, a dozen keys set from wherever needs them. This holds
    /// one object of each and writes it whole, and the difference matters in one direction only:
    /// a change that is never committed is a change that is lost. So the commits are named for
    /// what they write and are called where the thing is settled, rather than left to a timer.
    ///
    /// Reads and writes are synchronous. The files are a few hundred bytes of text and the only
    /// moments they are touched are a boot, a setting being changed, and the end of a run — an
    /// await would buy nothing and would make a screen's ordering depend on it.
    /// </remarks>
    public sealed class SaveVault
    {
        /// <summary>What the two files are filed under. Changing either abandons those saves.</summary>
        public const string ProgressKey = "relicrun.profile";

        public const string ChoicesKey = "relicrun.settings";

        private readonly ISaveService _saves;

        private SaveRepository<Progress> _progressFile;
        private SaveRepository<Choices> _choicesFile;

        private Progress _progress;
        private Choices _choices;

        /// <param name="saves">
        /// Where the two files live, or null for a vault that keeps everything in memory and
        /// forgets it.
        /// </param>
        /// <remarks>
        /// The null case is not a convenience for tests. It is what a screen gets when the
        /// application scope could not be reached, and it exists so that failure shows a delver
        /// who has never played rather than a screen that throws on its first read. Losing a
        /// save is bad; a game that will not open is worse, and only one of the two can be
        /// reported to anybody.
        /// </remarks>
        public SaveVault(ISaveService saves)
        {
            _saves = saves;
        }

        /// <summary>What the delver has earned, loading it first if that has not happened.</summary>
        /// <remarks>
        /// Lazy rather than loaded in a constructor, so nothing depends on when the container
        /// happens to build this. The first screen that asks is the first read.
        /// </remarks>
        public SaveState Earned
        {
            get { return OpenProgress().State; }
        }

        /// <summary>What the delver has chosen.</summary>
        public Preferences Chosen
        {
            get { return OpenChoices().Chosen; }
        }

        /// <summary>Writes the progression back to disk.</summary>
        public void CommitProgress()
        {
            SaveRepository<Progress> file = ProgressFile();

            if (file != null) file.Save(OpenProgress());
        }

        /// <summary>Writes the settings back to disk.</summary>
        public void CommitChoices()
        {
            SaveRepository<Choices> file = ChoicesFile();

            if (file != null) file.Save(OpenChoices());
        }

        /// <summary>Reads both files again, discarding what is held.</summary>
        /// <remarks>
        /// The way past the cache, which everything else goes through. A cache with no way past
        /// it is a save that cannot be reloaded after being replaced from somewhere else.
        /// </remarks>
        public void Reload()
        {
            _progress = null;
            _choices = null;
        }

        private Progress OpenProgress()
        {
            if (_progress != null) return _progress;

            SaveRepository<Progress> file = ProgressFile();

            // A repository that returns nothing is a package contract this does not rely on, and
            // a null here would fail later and somewhere else.
            _progress = file == null ? new Progress() : file.Load() ?? new Progress();

            if (_progress.Damaged > 0)
            {
                // Said out loud, once, because the alternative is a delver whose gold quietly
                // went back to zero and a log with nothing in it.
                Debug.LogWarning("[SaveVault] " + _progress.Damaged + " line(s) of the save could " +
                                 "not be read. Everything else was kept.");
            }

            return _progress;
        }

        private Choices OpenChoices()
        {
            if (_choices != null) return _choices;

            SaveRepository<Choices> file = ChoicesFile();

            _choices = file == null ? new Choices() : file.Load() ?? new Choices();

            return _choices;
        }

        private SaveRepository<Progress> ProgressFile()
        {
            if (_progressFile != null) return _progressFile;
            if (_saves == null) return null;

            _saves.Register<Progress>(ProgressKey);
            _progressFile = _saves.GetRepository<Progress>();

            return _progressFile;
        }

        private SaveRepository<Choices> ChoicesFile()
        {
            if (_choicesFile != null) return _choicesFile;
            if (_saves == null) return null;

            _saves.Register<Choices>(ChoicesKey);
            _choicesFile = _saves.GetRepository<Choices>();

            return _choicesFile;
        }
    }
}
