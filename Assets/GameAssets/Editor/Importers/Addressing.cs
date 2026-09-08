using System.Collections.Generic;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.AddressableAssets.Settings.GroupSchemas;
using UnityEngine;

namespace RelicRun.Editor.Importers
{
    /// <summary>
    /// Gives the content that is fetched on demand an address, which is its content id.
    /// </summary>
    /// <remarks>
    /// Only what is worth fetching. Measured uncompressed, the halls are four and a half
    /// megabytes and a run descends one of them; the event illustrations are two and a half and
    /// a delver sees one at a time. Against that, the relic sheet and the bestiary are one
    /// texture each and every screen draws from both, so addressing them would add a load to
    /// every frame that needed an icon and save nothing at all.
    ///
    /// The address IS the content id — <c>hall-hoard</c>, <c>event-chest</c>, <c>ja</c> — so
    /// the thing asking for a hall asks by the same name it uses everywhere else, and the join
    /// can be audited rather than trusted. That is what <c>AddressedContentTests</c> checks:
    /// every id has exactly one address, and no address names an id nothing asks for.
    /// </remarks>
    public static class Addressing
    {
        /// <summary>One group per kind, so a build can see where its bytes went.</summary>
        public const string HallGroup = "Relic Run Halls";

        public const string EventGroup = "Relic Run Events";
        public const string LocaleGroup = "Relic Run Locales";

        /// <summary>
        /// The scene prefabs, which are addressed because the scene service can only
        /// reach them that way.
        /// </summary>
        /// <remarks>
        /// Not a memory decision like the halls — a scene prefab is loaded once and stays.
        /// <c>SceneConfig</c> holds an <c>AssetReference</c> and nothing else, so a prefab
        /// that is not addressable is a scene that cannot be loaded at all.
        /// </remarks>
        public const string SceneGroup = "Relic Run Scenes";

        /// <summary>
        /// Puts one folder of assets into a group, addressed by the stem of each filename.
        /// </summary>
        /// <remarks>
        /// Returns the GUID for each id so the book can be filled from the same pass that did
        /// the addressing. Filling it from a second pass would mean two places deciding what an
        /// id means, and they would agree until the day one of them was changed.
        /// </remarks>
        public static Dictionary<string, string> Address(string group, string folder,
            IReadOnlyList<string> ids, string extension)
        {
            var guids = new Dictionary<string, string>();

            AddressableAssetSettings settings = AddressableAssetSettingsDefaultObject.Settings;
            if (settings == null)
            {
                Debug.LogError("no Addressables settings — open Window > Asset Management > " +
                               "Addressables > Groups once to create them");
                return guids;
            }

            AddressableAssetGroup into = Group(settings, group);

            foreach (string id in ids)
            {
                string path = folder + "/" + id + extension;
                string guid = AssetDatabase.AssetPathToGUID(path);

                if (string.IsNullOrEmpty(guid))
                {
                    Debug.LogError("nothing to address at " + path);
                    continue;
                }

                AddressableAssetEntry entry = settings.CreateOrMoveEntry(guid, into, false, false);
                if (entry == null)
                {
                    Debug.LogError("could not address " + path);
                    continue;
                }

                entry.address = id;
                guids[id] = guid;
            }

            settings.SetDirty(AddressableAssetSettings.ModificationEvent.BatchModification, null, true, true);
            return guids;
        }

        /// <summary>
        /// The group, made if it is not there, carrying the schemas a group needs to build.
        /// </summary>
        /// <remarks>
        /// A group with no <c>BundledAssetGroupSchema</c> is not an error and not a group
        /// either: Addressables skips it at build time and the assets it holds are simply
        /// absent, with nothing said about it until something asks for one at run time.
        /// </remarks>
        private static AddressableAssetGroup Group(AddressableAssetSettings settings, string name)
        {
            AddressableAssetGroup group = settings.FindGroup(name);
            if (group != null) return group;

            return settings.CreateGroup(name, false, false, false, null,
                typeof(BundledAssetGroupSchema), typeof(ContentUpdateGroupSchema));
        }

        /// <summary>Everything the game addresses, cleared out. Used when ids change.</summary>
        public static void Forget(string group)
        {
            AddressableAssetSettings settings = AddressableAssetSettingsDefaultObject.Settings;
            if (settings == null) return;

            AddressableAssetGroup found = settings.FindGroup(group);
            if (found == null) return;

            settings.RemoveGroup(found);
        }
    }
}
