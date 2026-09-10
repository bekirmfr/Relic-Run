using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RelicRun.Core.Content;

namespace RelicRun.Tests.Support
{
    /// <summary>
    /// Loads the golden corpus in <c>Tools/corpus/</c> — the contract every ported phase is
    /// diffed against. See <c>docs/port-plan.md</c> §6.
    /// </summary>
    /// <remarks>
    /// Works under both runners. <c>dotnet test</c> starts in the test project's output folder
    /// and Unity starts at the project root, so the directory is found by walking up rather than
    /// assuming either. Newtonsoft is used on purpose: Unity ships it as
    /// <c>com.unity.nuget.newtonsoft-json</c> and the dotnet project references the identical
    /// NuGet package, so these files need no conditional compilation.
    /// </remarks>
    public static class Corpus
    {
        private static string _root;

        /// <summary>Absolute path to <c>Tools/corpus</c>.</summary>
        public static string Root
        {
            get
            {
                if (_root != null)
                {
                    return _root;
                }

                foreach (string start in StartPoints())
                {
                    DirectoryInfo dir = new DirectoryInfo(start);
                    while (dir != null)
                    {
                        string candidate = Path.Combine(dir.FullName, "Tools", "corpus");
                        if (Directory.Exists(candidate))
                        {
                            _root = candidate;
                            return _root;
                        }

                        dir = dir.Parent;
                    }
                }

                throw new DirectoryNotFoundException(
                    "Could not locate Tools/corpus by walking up from " +
                    string.Join(" or ", StartPoints()) +
                    ". Generate it with: node Tools/capture/capture.mjs");
            }
        }

        private static IEnumerable<string> StartPoints()
        {
            yield return Directory.GetCurrentDirectory();
            yield return AppContext.BaseDirectory;
        }

        /// <summary>Raw text of a corpus file, e.g. <c>"bare.json"</c>.</summary>
        public static string ReadText(string fileName)
        {
            string path = Path.Combine(Root, fileName);
            if (!File.Exists(path))
            {
                throw new FileNotFoundException(
                    "Corpus file missing: " + path + ". Generate it with: node Tools/capture/capture.mjs");
            }

            return File.ReadAllText(path);
        }

        /// <summary>A corpus file parsed as a JSON array.</summary>
        public static JArray Array(string fileName)
        {
            return JArray.Parse(ReadText(fileName));
        }

        /// <summary>
        /// A generated content file from <c>Tools/out/</c> — relics, dungeons, events and the
        /// rest, extracted from the source game rather than recorded from a run.
        /// </summary>
        public static JArray ArrayFromContent(string fileName)
        {
            string path = Path.Combine(Path.GetDirectoryName(Root), "out", fileName);
            if (!File.Exists(path))
            {
                throw new FileNotFoundException(
                    "Content file missing: " + path + ". Generate it with: node Tools/extract/extract.mjs");
            }

            return JArray.Parse(File.ReadAllText(path));
        }

        /// <summary>A corpus file parsed as a JSON object.</summary>
        public static JObject Object(string fileName)
        {
            return JObject.Parse(ReadText(fileName));
        }

        /// <summary>A corpus file deserialized into <typeparamref name="T"/>.</summary>
        public static T Read<T>(string fileName)
        {
            return JsonConvert.DeserializeObject<T>(ReadText(fileName));
        }

        /// <summary>
        /// How big one of the shipped sprite sheets actually is, in pixels.
        /// </summary>
        /// <remarks>
        /// Read out of the PNG's own header rather than written down anywhere. The generated
        /// tables say how many cells a sheet has and how wide a cell is; the file says how wide
        /// the sheet is. Neither can check itself, and multiplying one out to see whether it
        /// equals the other is the only thing that catches a sheet that was re-exported a column
        /// wider — which would slice every icon after the first one slightly wrong, and look for
        /// all the world like an art mistake.
        ///
        /// IHDR is the first chunk of every PNG by the format's own rules, so the width and
        /// height sit at a fixed offset and no decoder is needed to read them.
        /// </remarks>
        public static void SheetSize(string fileName, out int width, out int height)
        {
            string path = Path.Combine(
                Path.GetDirectoryName(Path.GetDirectoryName(Root)), ".port", "assets", fileName);

            if (!File.Exists(path))
            {
                throw new FileNotFoundException("a shipped sprite sheet is missing: " + path);
            }

            byte[] header = new byte[24];
            using (FileStream file = File.OpenRead(path))
            {
                if (file.Read(header, 0, header.Length) != header.Length)
                {
                    throw new IOException(fileName + " is too short to be a PNG");
                }
            }

            width = Big(header, 16);
            height = Big(header, 20);
        }

        /// <summary>A big-endian 32-bit integer, as every number in a PNG header is.</summary>
        private static int Big(byte[] bytes, int at)
        {
            return (bytes[at] << 24) | (bytes[at + 1] << 16) | (bytes[at + 2] << 8) | bytes[at + 3];
        }

        /// <summary>
        /// The shipped hero pack, loaded from <c>.port/hero-pack.json</c>.
        /// </summary>
        /// <remarks>
        /// Read from the source drop rather than from a copy, so the tests compose the same art
        /// the game ships. The PARSING is <see cref="HeroPackReader"/>'s, which is also what the
        /// game runs: a second parser here would be a second answer to what the delver looks
        /// like, and the one that shipped would be whichever nobody was testing.
        /// </remarks>
        public static HeroPack HeroPack()
        {
            string path = Path.Combine(
                Path.GetDirectoryName(Path.GetDirectoryName(Root)), ".port", "hero-pack.json");

            if (!File.Exists(path))
            {
                throw new FileNotFoundException("the shipped hero pack is missing: " + path);
            }

            return HeroPackReader.Read(File.ReadAllText(path));
        }

        /// <summary>One language's strings, from <c>Tools/out/locales/</c>.</summary>
        public static Dictionary<string, string> LocaleTable(string language)
        {
            string path = Path.Combine(Path.GetDirectoryName(Root), "out", "locales",
                language + ".json");

            if (!File.Exists(path))
            {
                throw new FileNotFoundException("no such language: " + path);
            }

            var table = new Dictionary<string, string>();
            foreach (JProperty entry in JObject.Parse(File.ReadAllText(path)).Properties())
            {
                table[entry.Name] = entry.Value.Value<string>();
            }

            return table;
        }

    }
}
