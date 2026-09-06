using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

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
    }
}
