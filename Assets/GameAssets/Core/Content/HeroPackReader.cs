using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace RelicRun.Core.Content
{
    /// <summary>
    /// Turns the shipped <c>hero-pack.json</c> into a <see cref="HeroPack"/>.
    /// </summary>
    /// <remarks>
    /// Half a megabyte of pixel rows, which is why this is a reader and not a generator: every
    /// other content table in this port is turned into C# by <c>gen-csharp.mjs</c> and compiled,
    /// and doing that to the hero pack would be half a megabyte of string literals in a file
    /// nobody could open.
    ///
    /// In Core rather than in the game or in the tests, because both need it and both must get
    /// the SAME hero. A second parser would be a second answer to "what does the delver look
    /// like", and the one that shipped would be whichever one nobody was testing. Newtonsoft is
    /// the one package Core is allowed — see <c>Tools/check/asmrefs.py</c> — and this is what
    /// that exception was left open for.
    /// </remarks>
    public static class HeroPackReader
    {
        /// <summary>The pack, read from the file's text.</summary>
        /// <remarks>
        /// Text rather than a path. Core cannot reach a file the same way twice — under Unity the
        /// pack is an addressable <c>TextAsset</c> and under dotnet it is a file on disk — so
        /// whoever has it hands over what they read.
        /// </remarks>
        public static HeroPack Read(string json)
        {
            if (string.IsNullOrEmpty(json)) return null;

            JObject read = JObject.Parse(json);

            var pack = new HeroPack { Size = Size(read) };

            if (read["stack"] != null)
            {
                foreach (JToken slot in (JArray)read["stack"]) pack.Stack.Add(slot.Value<string>());
            }

            // The pack's own states are deliberately NOT read: they exist to validate the pack,
            // and the rig is what the game animates from.

            if (read["defaults"] != null)
            {
                foreach (JProperty pick in ((JObject)read["defaults"]).Properties())
                {
                    pack.Defaults[pick.Name] = pick.Value.Value<string>();
                }
            }

            if (read["famHex"] != null)
            {
                foreach (JProperty pick in ((JObject)read["famHex"]).Properties())
                {
                    if (pick.Name.Length > 0) pack.FamilyColours[pick.Name[0]] = pick.Value.Value<string>();
                }
            }

            // The base is a part like any other, kept apart in the file because every hero has
            // one and nothing chooses it.
            if (read["base"] != null && read["base"]["frames"] != null)
            {
                pack.Add(Part("base", "base", (JObject)read["base"]["frames"]));
            }

            if (read["parts"] != null)
            {
                foreach (JToken part in (JArray)read["parts"])
                {
                    pack.Add(Part(part["slot"].Value<string>(), part["id"].Value<string>(),
                        (JObject)part["frames"]));
                }
            }

            return pack;
        }

        /// <summary>
        /// What the delver wears with nothing chosen.
        /// </summary>
        /// <remarks>
        /// The pack's own defaults, which is what the source falls back to for every slot a
        /// delver has not picked in the wardrobe. There is no wardrobe in the save yet, so today
        /// this IS the outfit — and when there is one, this stays the floor under it.
        /// </remarks>
        public static IReadOnlyDictionary<string, string> Plain(HeroPack pack)
        {
            var worn = new Dictionary<string, string>();

            if (pack == null) return worn;

            foreach (KeyValuePair<string, string> pick in pack.Defaults) worn[pick.Key] = pick.Value;

            return worn;
        }

        private static int Size(JObject read)
        {
            return read["size"] != null ? read["size"].Value<int>() : HeroRig.DefaultSize;
        }

        private static HeroPart Part(string slot, string id, JObject frames)
        {
            var part = new HeroPart(slot, id);

            if (frames == null) return part;

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
