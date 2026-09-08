using System.IO;
using UnityEditor;
using UnityEngine;

namespace RelicRun.Editor.Importers
{
    /// <summary>
    /// Where the content comes from, and where it lands.
    /// </summary>
    /// <remarks>
    /// The source drop in <c>.port/</c> sits outside <c>Assets/</c> on purpose — it is the
    /// browser game, checked in whole, and Unity has no business importing a hundred files it
    /// will never draw. The importer copies across only what the catalogs actually ask for, so
    /// the project carries the art it uses rather than the archive it came from.
    /// </remarks>
    public static class ContentPaths
    {
        /* ---------- the source drop, relative to the project root ---------- */

        public const string SourceArt = ".port/assets";
        public const string SourceHeroPack = ".port/hero-pack.json";
        public const string SourceLocales = "Tools/out/locales";

        /* ---------- where it lands, as asset paths ---------- */

        public const string Art = "Assets/GameAssets/Art";
        public const string Sheets = Art + "/Sheets";
        public const string Halls = Art + "/Halls";
        public const string Events = Art + "/Events";

        public const string Content = "Assets/GameAssets/Content";
        public const string Text = Content + "/Text";
        public const string Locales = Text + "/Locales";

        public const string RelicIconSheet = Sheets + "/relic-icons.png";
        public const string EnemySheet = Sheets + "/enemies-hoard.png";
        public const string HeroPackText = Text + "/hero-pack.json";

        public const string RelicIconBookAsset = Content + "/RelicIcons.asset";
        public const string HallBookAsset = Content + "/HallArt.asset";
        public const string EventBookAsset = Content + "/EventArt.asset";
        public const string EnemyBookAsset = Content + "/EnemyArt.asset";
        public const string HeroPackAssetPath = Content + "/HeroPack.asset";
        public const string LocaleBookAsset = Content + "/Locales.asset";
        public const string PresentationAsset = Content + "/Presentation.asset";
        public const string GameContentAsset = Content + "/GameContent.asset";

        /// <summary>The folder holding <c>Assets/</c>, which is where the source drop lives.</summary>
        public static string ProjectRoot
        {
            get { return Directory.GetParent(Application.dataPath).FullName; }
        }

        /// <summary>An absolute path to something in the source drop.</summary>
        public static string Source(string relative)
        {
            return Path.Combine(ProjectRoot, relative.Replace('/', Path.DirectorySeparatorChar));
        }

        /// <summary>
        /// Makes sure a folder exists, parents and all.
        /// </summary>
        /// <remarks>
        /// Through the asset database rather than <c>Directory.CreateDirectory</c>. A folder made
        /// behind Unity's back has no <c>.meta</c> until the next refresh, and anything written
        /// into it in the meantime imports with a fresh GUID — which is how a re-run of an
        /// importer silently breaks every reference it made the time before.
        /// </remarks>
        public static void EnsureFolder(string assetPath)
        {
            if (AssetDatabase.IsValidFolder(assetPath)) return;

            int cut = assetPath.LastIndexOf('/');
            if (cut <= 0) return;

            string parent = assetPath.Substring(0, cut);
            EnsureFolder(parent);

            AssetDatabase.CreateFolder(parent, assetPath.Substring(cut + 1));
        }
    }
}
