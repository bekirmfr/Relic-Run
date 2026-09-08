using System.Collections.Generic;
using NUnit.Framework;
using RelicRun.Core.Content;
using RelicRun.Editor.Importers;
using RelicRun.Game.Data;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.AddressableAssets.Settings.GroupSchemas;
using UnityEngine;
using UnityEngine.AddressableAssets;

namespace RelicRun.Tests.Editor
{
    /// <summary>
    /// What is fetched on demand, and whether its addresses agree with the catalogs.
    /// </summary>
    /// <remarks>
    /// The address of a hall is its content id, so a screen asks for <c>hall-hoard</c> by the
    /// same name everything else uses. That is a convenience and a trap in equal measure: an
    /// address is a string, nothing resolves it until run time, and a typo is invisible until a
    /// delver reaches the ninth floor and the game hands them nothing. So the join is checked
    /// rather than trusted, in both directions — every id has exactly one address, and no
    /// address names an id nothing asks for.
    ///
    /// The split itself is asserted too. The sheets are deliberately NOT addressed: they are one
    /// texture each and every screen draws from both, so fetching them would add a load to every
    /// frame that needed an icon and save nothing. If somebody addresses them later that should
    /// be a decision, not a drift.
    /// </remarks>
    [TestFixture]
    public class AddressedContentTests
    {
        private GameContent _content;
        private AddressableAssetSettings _settings;

        [OneTimeSetUp]
        public void LoadTheContent()
        {
            _content = AssetDatabase.LoadAssetAtPath<GameContent>(ContentPaths.GameContentAsset);
            Assert.That(_content, Is.Not.Null, "run Tools > Relic Run > Import Content first");

            _settings = AddressableAssetSettingsDefaultObject.Settings;
            Assert.That(_settings, Is.Not.Null, "this project has no Addressables settings");
        }

        private AddressableAssetGroup Group(string name)
        {
            AddressableAssetGroup group = _settings.FindGroup(name);
            Assert.That(group, Is.Not.Null,
                name + " is missing — run Tools > Relic Run > Import Content");

            return group;
        }

        /// <summary>
        /// Each group can actually be built.
        /// </summary>
        /// <remarks>
        /// A group without a <c>BundledAssetGroupSchema</c> is not an error and not a group
        /// either. Addressables skips it at build time, its assets are simply absent, and nothing
        /// is said about it until something asks for one on a device.
        /// </remarks>
        [Test]
        public void EveryGroupCanBeBuilt()
        {
            foreach (string name in new[]
                     { Addressing.HallGroup, Addressing.EventGroup, Addressing.LocaleGroup })
            {
                Assert.That(Group(name).GetSchema<BundledAssetGroupSchema>(), Is.Not.Null,
                    name + " would be skipped at build time");
            }
        }

        [Test]
        public void EveryHallAndEventIsAddressedByItsContentId()
        {
            Exactly(Addressing.HallGroup, ContentIds.Halls);
            Exactly(Addressing.EventGroup, ContentIds.Events);
        }

        [Test]
        public void EveryLanguageIsAddressedByItsCode()
        {
            var languages = new List<string>();
            foreach (LocaleBook.Translation translation in _content.Locales.Languages)
            {
                languages.Add(translation.Language);
            }

            Assert.That(languages, Does.Contain(LocaleBook.Fallback), "no English to fall back to");
            Exactly(Addressing.LocaleGroup, languages);
        }

        /// <summary>
        /// One group holds exactly the ids asked for, addressed by those ids and nothing else.
        /// </summary>
        private void Exactly(string name, IReadOnlyList<string> ids)
        {
            var addressed = new Dictionary<string, int>();

            foreach (AddressableAssetEntry entry in Group(name).entries)
            {
                int seen;
                addressed.TryGetValue(entry.address, out seen);
                addressed[entry.address] = seen + 1;
            }

            var missing = new List<string>();
            var doubled = new List<string>();

            foreach (string id in ids)
            {
                int count;
                if (!addressed.TryGetValue(id, out count)) missing.Add(id);
                else if (count > 1) doubled.Add(id);
            }

            var unknown = new List<string>();
            foreach (KeyValuePair<string, int> address in addressed)
            {
                if (!Has(ids, address.Key)) unknown.Add(address.Key);
            }

            Assert.That(missing, Is.Empty, name + " has no address for: " + string.Join(", ", missing));
            Assert.That(doubled, Is.Empty, name + " addresses twice: " + string.Join(", ", doubled));
            Assert.That(unknown, Is.Empty,
                name + " addresses what nothing asks for: " + string.Join(", ", unknown));
        }

        private static bool Has(IReadOnlyList<string> ids, string id)
        {
            for (int i = 0; i < ids.Count; i++)
            {
                if (ids[i] == id) return true;
            }

            return false;
        }

        /// <summary>
        /// The books point at the same assets the addresses do.
        /// </summary>
        /// <remarks>
        /// Two ways to reach a hall — the book's reference and the group's address — and nothing
        /// makes them agree except having been filled in the same pass. If they ever drift, a
        /// screen that asks the book gets one picture and a screen that asks by address gets
        /// another, and both look correct on their own.
        /// </remarks>
        [Test]
        public void TheBooksAndTheAddressesPointAtTheSameAssets()
        {
            Agrees(_content.Halls, Addressing.HallGroup);
            Agrees(_content.Events, Addressing.EventGroup);

            foreach (LocaleBook.Translation translation in _content.Locales.Languages)
            {
                AssetReferenceT<TextAsset> strings = translation.Strings;
                Assert.That(strings, Is.Not.Null, translation.Language + " has no address");

                AddressableAssetEntry entry = _settings.FindAssetEntry(strings.AssetGUID);
                Assert.That(entry, Is.Not.Null, translation.Language + " is not addressed at all");
                Assert.That(entry.address, Is.EqualTo(translation.Language),
                    "the book calls it " + translation.Language + " and Addressables calls it " +
                    entry.address);
            }
        }

        private void Agrees(AddressBook book, string group)
        {
            foreach (AddressBook.Entry entry in book.Entries)
            {
                Assert.That(entry.Art, Is.Not.Null, entry.Id + " has no address");
                Assert.That(entry.Art.RuntimeKeyIsValid(), Is.True, entry.Id + " points nowhere");

                AddressableAssetEntry addressed = _settings.FindAssetEntry(entry.Art.AssetGUID);
                Assert.That(addressed, Is.Not.Null, entry.Id + " is not addressed at all");
                Assert.That(addressed.address, Is.EqualTo(entry.Id),
                    "the book calls it " + entry.Id + " and Addressables calls it " +
                    addressed.address);
                Assert.That(addressed.parentGroup.Name, Is.EqualTo(group),
                    entry.Id + " is addressed, but in " + addressed.parentGroup.Name);
            }
        }

        /// <summary>
        /// What is always wanted is not addressed, which is the other half of the decision.
        /// </summary>
        /// <remarks>
        /// Measured: the halls are four and a half megabytes uncompressed and a run descends one
        /// of them; the event illustrations are two and a half and a delver sees one at a time.
        /// The two sheets are one and a third between them and every screen draws from both.
        /// Addressing those would buy nothing and cost a load on the frame that wanted an icon.
        /// </remarks>
        [Test]
        public void TheSheetsAreResidentRatherThanAddressed()
        {
            foreach (string sheet in new[] { ContentPaths.RelicIconSheet, ContentPaths.EnemySheet })
            {
                string guid = AssetDatabase.AssetPathToGUID(sheet);
                Assert.That(guid, Is.Not.Empty, sheet + " is missing");

                Assert.That(_settings.FindAssetEntry(guid), Is.Null,
                    sheet + " has been addressed — every screen draws from it, so say why here " +
                    "if that was meant");
            }
        }
    }
}
