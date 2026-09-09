using RelicRun.Core.Run;
using RelicRun.Game.Data;
using UnityEditor;
using UnityEngine;

namespace RelicRun.Editor
{
    /// <summary>
    /// Draws a fight so that the parts which are not being used LOOK like it.
    /// </summary>
    /// <remarks>
    /// Written because of a specific hour lost. A foe had been authored — the right species, the
    /// right rank, fifty hit points, two relics — and the fight went on showing a cave bat,
    /// because the Override tick above it was off and the whole block was decoration. Every field
    /// was filled in, every value was correct, and none of it did anything.
    ///
    /// The default inspector cannot tell you that, because it has no idea one field governs
    /// another. So this greys out what is inert and says, in the space where the values are, what
    /// is being used instead. The rule it follows: a field that cannot affect anything should not
    /// look like a field that can.
    ///
    /// The disabled halves are still SHOWN rather than hidden. What is in them is the thing you
    /// are about to switch on, and hiding it would mean ticking a box to find out what ticking
    /// the box does.
    /// </remarks>
    [CustomEditor(typeof(FightSettings))]
    public sealed class FightSettingsEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            Fight();
            Delver();
            Foes();
            Watching();

            serializedObject.ApplyModifiedProperties();
        }

        private void Fight()
        {
            EditorGUILayout.LabelField("The fight", EditorStyles.boldLabel);
            Field("Seed");
            Field("Hall");
            Field("Floor");
        }

        /// <summary>
        /// The delver: a level, or numbers that replace it.
        /// </summary>
        /// <remarks>
        /// When the level is in charge, what it produces is shown in place of the greyed fields —
        /// otherwise "Level 4" is a number with no visible consequences, and the only way to
        /// learn what a delver at level four actually has is to run one.
        /// </remarks>
        private void Delver()
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("The delver", EditorStyles.boldLabel);

            SerializedProperty delver = serializedObject.FindProperty("Delver");
            SerializedProperty over = delver.FindPropertyRelative("OverrideStats");

            EditorGUILayout.PropertyField(delver.FindPropertyRelative("Level"));
            EditorGUILayout.PropertyField(over);

            using (new EditorGUI.DisabledScope(!over.boolValue))
            {
                foreach (string stat in new[] { "Hp", "Gold", "Atk", "Def", "Spd", "Lck" })
                {
                    EditorGUILayout.PropertyField(delver.FindPropertyRelative(stat));
                }
            }

            if (!over.boolValue)
            {
                RunSetup setup = RunSetup.ForLevel(delver.FindPropertyRelative("Level").intValue);

                EditorGUILayout.HelpBox(
                    "The level decides these. It gives " + setup.Hp + " hp, " + setup.Atk +
                    " atk, " + setup.Def + " def, " + setup.Spd + " spd, " + setup.Lck +
                    " lck. Tick Override Stats to use the numbers above instead.",
                    MessageType.None);
            }

            EditorGUILayout.PropertyField(delver.FindPropertyRelative("Shelf"), true);
        }

        /// <summary>
        /// The opposition, and the warning that cost an hour.
        /// </summary>
        /// <remarks>
        /// The important line is the one that appears when foes have been authored and Override
        /// is off. That is the exact state somebody sits in after typing a foe out in full, and
        /// the only signal the old inspector gave was that the fight looked wrong afterwards.
        /// </remarks>
        private void Foes()
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("The opposition", EditorStyles.boldLabel);

            SerializedProperty foes = serializedObject.FindProperty("Foes");
            SerializedProperty over = foes.FindPropertyRelative("Override");
            SerializedProperty pack = foes.FindPropertyRelative("Pack");

            EditorGUILayout.PropertyField(over);

            if (!over.boolValue)
            {
                EditorGUILayout.HelpBox(
                    pack.arraySize > 0
                        ? "These " + pack.arraySize + " foes are NOT being used. The floor " +
                          "generates its own pack — tick Override to fight the ones below."
                        : "The floor generates its own pack.",
                    pack.arraySize > 0 ? MessageType.Warning : MessageType.None);
            }
            else
            {
                EditorGUILayout.HelpBox(
                    "An authored pack changes the whole fight, not only who is in it: generating " +
                    "a pack draws from the same stream the fight then runs on, so the same seed " +
                    "describes a different fight with this on.", MessageType.Info);
            }

            using (new EditorGUI.DisabledScope(!over.boolValue))
            {
                EditorGUILayout.PropertyField(pack, true);
            }
        }

        private void Watching()
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Watching", EditorStyles.boldLabel);

            Field("SkipIntro");
            Field("Speed");
            Field("ReducedMotion");
        }

        private void Field(string name)
        {
            EditorGUILayout.PropertyField(serializedObject.FindProperty(name));
        }
    }
}
