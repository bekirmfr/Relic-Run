using System.Collections.Generic;
using System.IO;
using GameLift.Audio;
using RelicRun.Core.Presentation;
using UnityEditor;
using UnityEngine;

namespace RelicRun.Editor.Importers
{
    /// <summary>
    /// Renders the game's eight noises and binds them where the audio service can find them.
    /// </summary>
    /// <remarks>
    /// Generated, like everything else here, and for the strongest reason of any of it: there is
    /// nothing to import. The source ships no audio files — its sounds are an oscillator and a
    /// gain envelope, and the eight blips are eight rows of arguments to a thirty-line function.
    /// A folder of hand-made WAVs would be somebody's impression of those numbers rather than the
    /// numbers, and it would drift the first time anybody adjusted one.
    ///
    /// So the synthesis is ported into Core, where <c>dotnet test</c> can hear it — an envelope
    /// that clicks or a slide that runs the wrong way is one frame of sound and an obvious array
    /// — and this writes what it produces out as WAVs.
    /// </remarks>
    public static class SoundImporter
    {
        /// <summary>Where the rendered clips and their SoundData live.</summary>
        public const string Folder = "Assets/GameAssets/Audio";

        /// <summary>
        /// CD rate, which is far more than these need and the only rate nothing resamples.
        /// </summary>
        /// <remarks>
        /// The highest note here is the gold blip sliding to 1320Hz, so eight thousand samples a
        /// second would carry all of it. Forty-four thousand costs a few kilobytes for eight
        /// sounds none of which lasts half a second, and it is what every device plays natively.
        /// </remarks>
        public const int Rate = 44100;

        [MenuItem("Tools/Relic Run/Import Sounds", priority = 102)]
        public static void Import()
        {
            ContentPaths.EnsureFolder(Folder);

            var made = new List<SoundData>();

            foreach (Blip blip in Blips.All)
            {
                AudioClip clip = Write(blip);
                if (clip == null) continue;

                made.Add(Bind(blip, clip));
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log("rendered " + made.Count + " sounds into " + Folder +
                      ". They are synthesised from the source's own numbers, so there is nothing " +
                      "to redraw and nothing to keep in step.");
        }

        /// <summary>
        /// One blip, as a WAV on disk.
        /// </summary>
        /// <remarks>
        /// A file rather than an <c>AudioClip.Create</c> at run time, because a clip built in
        /// memory cannot be referenced by an asset — and the audio service plays what a
        /// <c>SoundData</c> points at.
        /// </remarks>
        private static AudioClip Write(Blip blip)
        {
            float[] samples = blip.Render(Rate);
            string path = Folder + "/" + blip.Name + ".wav";

            File.WriteAllBytes(Path.Combine(ContentPaths.ProjectRoot, path), Wav(samples, Rate));
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);

            var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);

            if (clip == null) Debug.LogError("could not render " + blip.Name);

            return clip;
        }

        /// <summary>
        /// The <c>SoundData</c> the audio service looks up, made once and then left alone.
        /// </summary>
        /// <remarks>
        /// Only the clip is rewritten on a re-import. Volume, pitch and channel are things a
        /// person sets by listening, and a generator that reset them every run would undo that
        /// work every time somebody re-rendered the waveforms.
        /// </remarks>
        private static SoundData Bind(Blip blip, AudioClip clip)
        {
            string path = Folder + "/" + blip.Name + ".asset";
            var data = AssetDatabase.LoadAssetAtPath<SoundData>(path);
            bool fresh = data == null;

            if (fresh)
            {
                data = ScriptableObject.CreateInstance<SoundData>();
                data.soundName = blip.Name;

                // The tap is the interface answering a press; the rest are the fight. The service
                // can silence one without the other, and a delver who muted the game's noises
                // should still hear their own buttons.
                data.soundType = blip.Name == Blips.Tap ? SoundType.UI : SoundType.SFX;
            }

            data.clip = clip;

            if (fresh) AssetDatabase.CreateAsset(data, path);
            else EditorUtility.SetDirty(data);

            return data;
        }

        /// <summary>
        /// Sixteen-bit mono PCM, which is the plainest thing Unity will import.
        /// </summary>
        /// <remarks>
        /// Written by hand because a WAV header is forty-four bytes and the alternative is a
        /// dependency. Sixteen bits is far more than these need — a square wave at a twentieth of
        /// full scale uses a fraction of the range — but it is the format nothing has to be told
        /// about, and eight clips of under half a second is tens of kilobytes either way.
        /// </remarks>
        private static byte[] Wav(float[] samples, int rate)
        {
            const int Channels = 1;
            const int Bits = 16;

            int bytes = samples.Length * 2;

            using (var memory = new MemoryStream(44 + bytes))
            using (var write = new BinaryWriter(memory))
            {
                write.Write(new[] { 'R', 'I', 'F', 'F' });
                write.Write(36 + bytes);
                write.Write(new[] { 'W', 'A', 'V', 'E' });

                write.Write(new[] { 'f', 'm', 't', ' ' });
                write.Write(16);
                write.Write((short)1);
                write.Write((short)Channels);
                write.Write(rate);
                write.Write(rate * Channels * Bits / 8);
                write.Write((short)(Channels * Bits / 8));
                write.Write((short)Bits);

                write.Write(new[] { 'd', 'a', 't', 'a' });
                write.Write(bytes);

                foreach (float sample in samples)
                {
                    // Clamped, so a gain somebody raised cannot wrap a peak into its opposite —
                    // which does not sound loud, it sounds broken.
                    float held = Mathf.Clamp(sample, -1f, 1f);

                    write.Write((short)Mathf.RoundToInt(held * short.MaxValue));
                }

                write.Flush();

                return memory.ToArray();
            }
        }
    }
}
