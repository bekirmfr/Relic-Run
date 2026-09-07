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
        /// The shipped hero pack, loaded from <c>.port/hero-pack.json</c>.
        /// </summary>
        /// <remarks>
        /// Read from the source drop rather than from a copy, so the tests compose the same art
        /// the game ships. Core cannot parse JSON — it references nothing — so the reading
        /// happens here and Core is handed a filled <see cref="HeroPack"/>. A Unity importer
        /// will fill the same object from the same file.
        /// </remarks>
        public static HeroPack HeroPack()
        {
            string path = Path.Combine(
                Path.GetDirectoryName(Path.GetDirectoryName(Root)), ".port", "hero-pack.json");

            if (!File.Exists(path))
            {
                throw new FileNotFoundException("the shipped hero pack is missing: " + path);
            }

            JObject json = JObject.Parse(File.ReadAllText(path));
            var pack = new RelicRun.Core.Content.HeroPack { Size = json["size"].Value<int>() };

            foreach (JToken slot in (JArray)json["stack"]) pack.Stack.Add(slot.Value<string>());

            // The pack's own states are deliberately NOT read: they exist to validate the pack,
            // and the rig is what the game animates from.

            if (json["defaults"] != null)
            {
                foreach (JProperty pick in ((JObject)json["defaults"]).Properties())
                {
                    pack.Defaults[pick.Name] = pick.Value.Value<string>();
                }
            }

            if (json["famHex"] != null)
            {
                foreach (JProperty pick in ((JObject)json["famHex"]).Properties())
                {
                    pack.FamilyColours[pick.Name[0]] = pick.Value.Value<string>();
                }
            }

            if (json["base"] != null && json["base"]["frames"] != null)
            {
                pack.Add(PartFrom("base", "base", (JObject)json["base"]["frames"]));
            }

            foreach (JToken part in (JArray)json["parts"])
            {
                pack.Add(PartFrom(part["slot"].Value<string>(), part["id"].Value<string>(),
                    (JObject)part["frames"]));
            }

            return pack;
        }

        private static HeroPart PartFrom(string slot, string id, JObject frames)
        {
            var part = new HeroPart(slot, id);

            foreach (JProperty state in frames.Properties())
            {
                var list = new List<string[]>();
                foreach (JToken frame in (JArray)state.Value)
                {
                    var rows = new List<string>();
                    foreach (JToken row in (JArray)frame) rows.Add(row.Value<string>());
                    list.Add(rows.ToArray());
                }

                part.Frames[state.Name] = list;
            }

            return part;
        }
    }
}
