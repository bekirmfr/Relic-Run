using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using RelicRun.Core.Presentation;

namespace RelicRun.Tests
{
    /// <summary>
    /// Nothing in Core is named after something the engine already put in scope.
    /// </summary>
    /// <remarks>
    /// Core has no engine reference, so nothing here can collide with anything — until a widget
    /// writes <c>using RelicRun.Core.Presentation;</c> next to <c>using UnityEngine;</c>, at which
    /// point every name Core exports is competing with every name the engine exports, and C#
    /// refuses to guess.
    ///
    /// This exists because a <c>Screen</c> enum was added to Core.Presentation and broke
    /// <c>PixelCanvas</c>, which had been reading <c>Screen.width</c> quite happily for a phase
    /// and a half. Four compile errors in a file nobody had touched.
    ///
    /// The shape of that failure is what makes it worth a gate. It cannot be caught by
    /// <c>dotnet test</c> noticing anything — Core compiles perfectly, the tests pass, the whole
    /// suite is green — and it surfaces only when the Unity assemblies build, which is somewhere
    /// else and later. So the check is written HERE, where it runs against a list rather than
    /// against the engine, and fails in the runner that can be run.
    /// </remarks>
    [TestFixture]
    public class CoreNamesTests
    {
        /// <summary>
        /// Types <c>using UnityEngine;</c> brings into scope that a game is likely to want.
        /// </summary>
        /// <remarks>
        /// A list rather than the real assembly, because this fixture is compiled by a runner
        /// that has no engine to ask. It is deliberately not exhaustive — <c>UnityEngine</c>
        /// exports hundreds of types and most are named things nobody would call a rule. What is
        /// here is the ones a port of a game might plausibly reach for, which is the only band
        /// where a collision is going to happen.
        ///
        /// Adding to it costs nothing. Leaving something out costs one compile error in a file
        /// nobody has touched, which is what it cost the first time.
        /// </remarks>
        private static readonly string[] Engine =
        {
            "Animation", "Animator", "Application", "AudioClip", "Behaviour", "Bounds", "Camera",
            "Canvas", "Collider", "Color", "Color32", "Component", "Cursor", "Debug", "Display",
            "Event", "Font", "Gradient", "Input", "Joint", "Keyframe", "Light", "Material",
            "Mathf", "Matrix4x4", "Mesh", "Motion", "Object", "Physics", "Plane", "Pose",
            "Quaternion", "Random", "Range", "Ray", "Rect", "Renderer", "Resolution", "Resources",
            "Rigidbody", "Scene", "Screen", "Shader", "Space", "Sprite", "Terrain", "Texture",
            "Time", "Touch", "Transform", "Tree", "Vector2", "Vector2Int", "Vector3", "Vector4",
        };

        /// <summary>Every public type Core exports, checked against that list.</summary>
        [Test]
        public void NoCoreTypeShadowsAnEngineType()
        {
            var engine = new HashSet<string>(Engine);
            var clashing = new List<string>();

            foreach (Type type in Core())
            {
                if (engine.Contains(type.Name)) clashing.Add(type.FullName);
            }

            Assert.That(clashing, Is.Empty,
                "these are named after types UnityEngine already puts in scope, so any widget " +
                "importing both stops compiling: " + string.Join(", ", clashing));
        }

        /// <summary>And Core really was asked, rather than an empty list being asked.</summary>
        /// <remarks>
        /// The test above passes trivially if the reflection finds nothing, and reflection
        /// finding nothing is exactly what a renamed assembly or a changed namespace would do.
        /// A gate that cannot fail is not a gate.
        /// </remarks>
        [Test]
        public void TheCheckIsActuallyLookingAtCore()
        {
            var found = new List<Type>(Core());

            Assert.That(found.Count, Is.GreaterThan(100),
                "only " + found.Count + " public types found in Core, so this checked nothing");

            var names = new List<string>();
            foreach (Type type in found) names.Add(type.Name);

            Assert.That(names, Does.Contain("Page"),
                "the type this whole fixture exists because of is not among them");
        }

        private static IEnumerable<Type> Core()
        {
            Assembly core = typeof(Page).Assembly;

            foreach (Type type in core.GetExportedTypes())
            {
                // Nested types are reached through their parent's name, so they cannot collide.
                if (type.IsNested) continue;

                yield return type;
            }
        }
    }
}
