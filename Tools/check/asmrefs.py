"""
Assembly references — does every file's imports actually reach it?

    python Tools/check/asmrefs.py

The gap this fills is specific and was found the expensive way, twice. Core and its tests are
compiled by `dotnet test`, so a mistake in them is caught here in seconds. The Unity assemblies —
RelicRun.Game, RelicRun.Editor, RelicRun.Tests.Editor — are compiled ONLY by Unity, which is
somewhere else and later. A builder that names `VContainer.Unity.LifetimeScope` from an assembly
with no VContainer reference is perfectly good C# and a perfectly green test run, right up until
the editor tries to build it.

So this reads every asmdef, works out which assembly each namespace belongs to, and reports any
file importing something its own assembly cannot see. It is a linker's question asked by hand,
and it is worth asking by hand precisely because the linker is not available here.

What it does NOT do is understand C#. It reads `using` directives and fully-qualified names of
the roots it knows about. That is enough for the failure it exists for — an assembly reference
that was never added — and it will not notice a type that moved between namespaces inside an
assembly that IS referenced. Those the compiler catches on both sides.
"""

import json
import os
import re
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

# Namespace root -> the asmdef name that has to be referenced to use it.
#
# Only the ones this project actually pulls in. A root that is not here is assumed to be free —
# the engine, the editor, the BCL — which is the right default: everything that needs listing in
# an asmdef is something somebody deliberately added to the project.
OWNED = {
    "RelicRun.Core": "RelicRun.Core",
    "RelicRun.Game": "RelicRun.Game",
    "RelicRun.Editor": "RelicRun.Editor",
    "GameLift": "GameLift",
    "VContainer": "VContainer",
    "Cysharp": "UniTask",
    "TMPro": "Unity.TextMeshPro",
    "NUnit": "UnityEngine.TestRunner",
}

# Assemblies Unity auto-references into every assembly that does not opt out, so importing them
# needs nothing. Newtonsoft is the surprising one and is why it is written down: it is a package,
# it looks exactly like something that would need listing, and it does not.
FREE = {"Newtonsoft"}

# Roots that resolve inside the assembly being checked, or to the engine and the framework.
IMPLICIT = ("System", "UnityEngine", "UnityEditor", "Unity.")


def asmdefs():
    """Every asmdef under Assets/GameAssets, by the folder it governs."""
    found = []

    for where, _, files in os.walk(os.path.join(ROOT, "Assets", "GameAssets")):
        for name in files:
            if not name.endswith(".asmdef"):
                continue

            path = os.path.join(where, name)

            with open(path, encoding="utf8") as handle:
                found.append((where, json.load(handle)))

    return found


def owner(namespace):
    """Which assembly a namespace needs, or None when nothing needs listing."""
    for prefix, assembly in OWNED.items():
        if namespace == prefix or namespace.startswith(prefix + "."):
            return assembly

    return None


def imports(source):
    """Every namespace a file reaches for: its usings, and any qualified name of a known root."""
    wanted = set()

    for line in re.findall(r"^\s*using (?:static )?([\w.]+);", source, re.M):
        wanted.add(line)

    # Comments first, or a note about VContainer reads as a reference to it.
    body = re.sub(r"^\s*///.*", "", source, flags=re.M)
    body = re.sub(r"//.*", "", body)
    body = re.sub(r"/\*.*?\*/", "", body, flags=re.S)
    body = re.sub(r'"(?:[^"\\]|\\.)*"', '""', body)

    for prefix in OWNED:
        if re.search(r"\b" + re.escape(prefix) + r"\.", body):
            wanted.add(prefix)

    return wanted


def main():
    problems = []
    checked = 0

    for folder, asm in asmdefs():
        name = asm["name"]
        references = set(asm.get("references", []))

        # An asmdef may reference by GUID rather than by name, which this cannot resolve.
        if any(ref.startswith("GUID:") for ref in references):
            print("skipping " + name + ": it references assemblies by GUID")
            continue

        for where, _, files in os.walk(folder):
            # A nested asmdef governs its own folder.
            if where != folder and any(f.endswith(".asmdef") for f in os.listdir(where)):
                continue

            for file in files:
                if not file.endswith(".cs"):
                    continue

                path = os.path.join(where, file)
                checked += 1

                with open(path, encoding="utf8") as handle:
                    source = handle.read()

                for namespace in sorted(imports(source)):
                    root = namespace.split(".")[0]

                    if root in FREE or namespace.startswith(IMPLICIT):
                        continue

                    needs = owner(namespace)

                    if needs is None or needs == name or needs in references:
                        continue

                    problems.append(
                        (os.path.relpath(path, ROOT).replace(os.sep, "/"), namespace, needs, name))

    print("\nassembly references")
    print("  checked " + str(checked) + " files across " + str(len(asmdefs())) + " assemblies")

    if not problems:
        print("  every import reaches its assembly")
        return 0

    print("\n  " + str(len(problems)) + " import(s) their assembly cannot see:\n")

    for path, namespace, needs, name in problems:
        print("    " + path)
        print("      uses " + namespace + ", which needs " + needs +
              " in " + name + "'s references")

    print("\n  This is a Unity compile error, and only Unity will report it.")
    return 1


if __name__ == "__main__":
    sys.exit(main())
