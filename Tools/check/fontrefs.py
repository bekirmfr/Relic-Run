"""
Font sub-assets — does every reference to one still land on something?

    python Tools/check/fontrefs.py

A TMP font asset is three objects in one file: the face, its atlas texture, and its material.
The face is the main object and gets the fixed local id 11400000; the other two are sub-assets
and get whatever local ids Unity felt like handing out when they were added. Every label in the
game points at the material — `m_sharedMaterial: {fileID: 8251982118749880301, guid: <font>,
type: 2}` — so those arbitrary ids are load-bearing in about forty prefabs.

The importer used to delete the font asset and make a new one on every run, which handed out new
ids every time, which pointed every one of those references at a sub-asset that no longer
existed. Nothing errored. TMP quietly falls back to its default material, and the game renders in
a face nobody chose, which is the sort of thing that survives a code review and gets noticed in a
screenshot three days later. It happened twice, and both times it was patched by regenerating the
scenes rather than by stopping the ids from moving.

So this asks the question directly: for every reference to a font asset anywhere under Assets, is
the local id it names actually defined in that font asset? It is a link check, and it does not
care why a link broke — a re-bake, a hand edit, a merge that took one side of a prefab and the
other side of the atlas. Any of those and this goes red.

It does NOT check that the reference points at the RIGHT sub-asset: an atlas id where a material
was meant is a live reference and passes here. The compiler for that is a screenshot.
"""

import os
import re
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
ASSETS = os.path.join(ROOT, "Assets")

# Where a reference to a font asset can be written down. Unity's other text formats can hold one
# too, but these are the four the project actually keeps under source control.
LOOKED_AT = (".prefab", ".unity", ".asset", ".mat")

# The head of a YAML document: `--- !u!114 &11400000`. The number after the ampersand is the
# local id everything else in the project uses to name that object.
DEFINES = re.compile(r"^--- !u!\d+ &(-?\d+)", re.M)

# A reference out of one file and into another: `{fileID: <local id>, guid: <file>, type: <n>}`.
REFERS = re.compile(r"fileID: (-?\d+), guid: ([0-9a-f]{32}), type: \d+")

GUID = re.compile(r"^guid: ([0-9a-f]{32})", re.M)

# What makes a .asset a TMP font asset. By its fields rather than by its script guid, so a
# TextMeshPro version bump does not quietly turn this check off.
FACE = ("m_FaceInfo:", "m_AtlasPopulationMode:")


def read(path):
    """A text-serialized asset, or None if it turns out not to be text."""
    try:
        with open(path, encoding="utf8", errors="strict") as handle:
            return handle.read()
    except (UnicodeDecodeError, OSError):
        return None


def walk():
    """Every file under Assets this cares about, as (path, text)."""
    for where, _, files in os.walk(ASSETS):
        for name in files:
            if not name.endswith(LOOKED_AT):
                continue

            path = os.path.join(where, name)
            body = read(path)

            if body is not None:
                yield path, body


def fonts(files):
    """Every font asset, as guid -> (path, the local ids it defines)."""
    found = {}

    for path, body in files:
        if not path.endswith(".asset") or not all(field in body for field in FACE):
            continue

        meta = read(path + ".meta")
        if meta is None:
            continue

        guid = GUID.search(meta)
        if guid is None:
            continue

        found[guid.group(1)] = (path, set(DEFINES.findall(body)))

    return found


def main():
    files = list(walk())
    faces = fonts(files)

    dangling = []
    checked = 0

    for path, body in files:
        for number, line in enumerate(body.splitlines(), 1):
            for local, guid in REFERS.findall(line):
                if guid not in faces:
                    continue

                checked += 1

                # `fileID: 0` is how a null gets written when the guid is still remembered.
                if local == "0" or local in faces[guid][1]:
                    continue

                dangling.append((os.path.relpath(path, ROOT).replace(os.sep, "/"),
                                 number, local, faces[guid][0]))

    print("\nfont sub-asset references")
    print("  checked " + str(checked) + " reference(s) to " + str(len(faces)) +
          " font asset(s) across " + str(len(files)) + " files")

    if not dangling:
        print("  every one of them lands on an object that exists")
        return 0

    print("\n  " + str(len(dangling)) + " reference(s) point at nothing:\n")

    for path, number, local, face in sorted(dangling):
        print("    " + path + ":" + str(number))
        print("      fileID " + local + " is not defined in " +
              os.path.relpath(face, ROOT).replace(os.sep, "/"))

    print("\n  The font asset was rebuilt and its sub-assets were handed new ids. TMP falls")
    print("  back silently, so nothing will report this at run time. See FontImporter.Recipe.")
    return 1


if __name__ == "__main__":
    sys.exit(main())
