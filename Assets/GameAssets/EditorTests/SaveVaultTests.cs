using System.Collections.Generic;
using System.Threading.Tasks;
using GameLift.Save;
using NUnit.Framework;
using RelicRun.Core.Meta;
using RelicRun.Game.Services;

namespace RelicRun.Tests.Editor
{
    /// <summary>
    /// A delver's progression, through the save service and back.
    /// </summary>
    /// <remarks>
    /// The codec itself is gated in Core, where <c>dotnet test</c> runs it against nasty names
    /// and torn files. What is left to check is the seam — the package's <c>ISaveable</c>, whose
    /// <c>Deserialize&lt;T&gt;</c> is generic over a type it then casts, and a repository that
    /// caches. Those are the two places a save can be written, read back, and still come out as
    /// a delver who has never played.
    ///
    /// Run against an in-memory handler rather than the real files: this is testing the wiring,
    /// and a test that wrote to persistentDataPath would overwrite the save of whoever ran it.
    /// </remarks>
    [TestFixture]
    public class SaveVaultTests
    {
        /// <summary>What is saved is what comes back.</summary>
        /// <remarks>
        /// The whole point of the class, and the failure it guards is silent in the worst way:
        /// a save that writes and never reads looks exactly like a delver's first run.
        /// </remarks>
        [Test]
        public void WhatIsCommittedIsWhatLoads()
        {
            var disk = new Shelf();
            var vault = new SaveVault(new Saves(disk));

            vault.Earned.Gold = 340;
            vault.Earned.Runs = 12;
            vault.Earned.Unlocked = 4;
            vault.Earned.Scores.Add(new ScoreRow { Score = 2870, Name = "Bekir", Floor = 13 });

            vault.Chosen.Name = "Bekir";
            vault.Chosen.Language = "tr";
            vault.Chosen.Muted = true;

            vault.CommitProgress();
            vault.CommitChoices();

            var opened = new SaveVault(new Saves(disk));

            Assert.That(opened.Earned.Gold, Is.EqualTo(340));
            Assert.That(opened.Earned.Runs, Is.EqualTo(12));
            Assert.That(opened.Earned.Unlocked, Is.EqualTo(4));
            Assert.That(opened.Earned.Scores.Count, Is.EqualTo(1));
            Assert.That(opened.Earned.Scores[0].Name, Is.EqualTo("Bekir"));

            Assert.That(opened.Chosen.Name, Is.EqualTo("Bekir"));
            Assert.That(opened.Chosen.Language, Is.EqualTo("tr"));
            Assert.That(opened.Chosen.Muted, Is.True);
        }

        /// <summary>
        /// The two files are separate, so losing one does not cost the other.
        /// </summary>
        /// <remarks>
        /// The reason for two files rather than one. A delver who wipes their progress keeps
        /// their name and their language; one whose progression file is torn still opens the
        /// game in a language they can read.
        /// </remarks>
        [Test]
        public void SettingsSurviveAProgressionThatDoesNot()
        {
            var disk = new Shelf();
            var vault = new SaveVault(new Saves(disk));

            vault.Chosen.Name = "Bekir";
            vault.Chosen.Language = "tr";
            vault.Earned.Gold = 340;

            vault.CommitProgress();
            vault.CommitChoices();

            disk.Wipe(SaveVault.ProgressKey);

            var opened = new SaveVault(new Saves(disk));

            Assert.That(opened.Earned.Gold, Is.Zero, "the progression is gone, as asked");
            Assert.That(opened.Chosen.Name, Is.EqualTo("Bekir"), "and the name did not go with it");
            Assert.That(opened.Chosen.Language, Is.EqualTo("tr"));
        }

        /// <summary>
        /// The generic deserialize really does hand back a Profile.
        /// </summary>
        /// <remarks>
        /// <c>ISaveable.Deserialize&lt;T&gt;</c> returns a T that the implementation has to cast
        /// to, which is the package's shape and not a choice. A cast is exactly the sort of thing
        /// that compiles and then throws once, at boot, on a device.
        /// </remarks>
        [Test]
        public void ASavedProfileDeserializesAsItself()
        {
            var progress = new Progress();
            progress.State.Xp = 18400;

            Progress back = new Progress().Deserialize<Progress>(progress.Serialize());

            Assert.That(back, Is.Not.Null);
            Assert.That(back.State.Xp, Is.EqualTo(18400));
            Assert.That(back.Version, Is.EqualTo(SaveCodec.Version));
            Assert.That(back.Damaged, Is.Zero);

            var choices = new Choices();
            choices.Chosen.Name = "Bekir";

            Choices chosen = new Choices().Deserialize<Choices>(choices.Serialize());

            Assert.That(chosen, Is.Not.Null);
            Assert.That(chosen.Chosen.Name, Is.EqualTo("Bekir"));
        }

        /// <summary>Nothing on disk is a delver who has not played.</summary>
        [Test]
        public void AnEmptyDiskOpensAsAFreshDelver()
        {
            var vault = new SaveVault(new Saves(new Shelf()));

            Assert.That(vault.Earned, Is.Not.Null);
            Assert.That(vault.Earned.Runs, Is.Zero);
            Assert.That(vault.Earned.Unlocked, Is.EqualTo(1), "the first hall is always open");

            Assert.That(vault.Chosen, Is.Not.Null);
            Assert.That(vault.Chosen.NeverAsked, Is.True, "and they have not been asked their name");
        }

        /// <summary>
        /// Loading again reads the file rather than handing back what is held.
        /// </summary>
        /// <remarks>
        /// The vault caches, deliberately, so every screen asking for the save does not touch the
        /// disk. <see cref="SaveVault.Reload"/> is the way past that cache, and a cache with no
        /// way past it is a save that cannot be reloaded after being replaced.
        /// </remarks>
        [Test]
        public void LoadingAgainGoesBackToTheDisk()
        {
            var disk = new Shelf();
            var vault = new SaveVault(new Saves(disk));

            vault.Earned.Gold = 10;
            vault.CommitProgress();

            // Somebody else writes over it — a cloud restore, or a second vault.
            var other = new SaveVault(new Saves(disk));
            other.Earned.Gold = 999;
            other.CommitProgress();

            Assert.That(vault.Earned.Gold, Is.EqualTo(10), "the cache should still be the cache");

            vault.Reload();

            Assert.That(vault.Earned.Gold, Is.EqualTo(999));
        }

        /// <summary>A file on disk, without a disk.</summary>
        private sealed class Shelf : ISaveHandler
        {
            private readonly Dictionary<string, string> _files = new Dictionary<string, string>();

            public void SaveData(string key, string data) { _files[key] = data; }

            public string LoadData(string key)
            {
                string data;
                return _files.TryGetValue(key, out data) ? data : null;
            }

            public bool CheckKeyExist(string key) { return _files.ContainsKey(key); }

            /// <summary>Loses one file, the way a wipe or a bad sector would.</summary>
            public void Wipe(string key) { _files.Remove(key); }

            public Task SaveDataAsync(string key, string data)
            {
                SaveData(key, data);
                return Task.CompletedTask;
            }

            public Task<string> LoadDataAsync(string key)
            {
                return Task.FromResult(LoadData(key));
            }
        }

        /// <summary>
        /// The save service, without the singleton.
        /// </summary>
        /// <remarks>
        /// The package's own <c>SaveService</c> throws if a second one is built, which is fine
        /// for a game and impossible for a fixture that builds one per test.
        /// </remarks>
        private sealed class Saves : ISaveService
        {
            private readonly ISaveHandler _handler;
            private readonly Dictionary<string, object> _repositories =
                new Dictionary<string, object>();

            public Saves(ISaveHandler handler) { _handler = handler; }

            public PrimitiveSaveHelper Raw
            {
                get { return null; }
            }

            public void Register<T>(string key) where T : ISaveable, new()
            {
                string name = typeof(T).FullName;
                if (_repositories.ContainsKey(name)) return;

                _repositories[name] = new SaveRepository<T>(_handler, key);
            }

            public SaveRepository<T> GetRepository<T>() where T : ISaveable, new()
            {
                object repository;
                return _repositories.TryGetValue(typeof(T).FullName, out repository)
                    ? repository as SaveRepository<T>
                    : null;
            }
        }
    }
}
