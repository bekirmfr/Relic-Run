"""Mutation testing, run somewhere Unity cannot see it.

    python Tools/mutate/mutate.py Tools/mutate/<suite>.json

A gate that cannot fail is not a gate, so every rule the port claims to hold is checked by
breaking it on purpose and demanding that the tests notice. That means editing source, running
the suite, and putting the source back — dozens of times.

DO NOT DO THAT IN THE PROJECT. Unity watches the filesystem and recompiles what it finds. A
mutation that lives for the two seconds a test run takes is long enough for the editor to pick
it up, and it will then report a failure in code that has already been put back — a bug that
does not exist, in a file that is correct on disk and in git. That happened once and cost a
round trip to diagnose.

So the sources are copied out first and every mutation is applied to the copy. The project is
never written to at all, which is a stronger guarantee than restoring carefully: there is
nothing to restore.

A suite is JSON:

    {
      "name": "the run layer",
      "mutants": [
        { "name": "a floor is worth 39", "file": "Assets/...cs",
          "find": "PerFloor = 40;", "replace": "PerFloor = 39;" }
      ]
    }

Each mutant is applied alone, to a fresh copy of the file, and must make the suite FAIL. One
that survives is either a rule nothing tests, or a rule that cannot be observed at all — the
two are worth telling apart, and the report says which mutants lived so that can be judged.
"""

import json
import os
import shutil
import subprocess
import sys
import tempfile

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

# What the dotnet test project actually needs: the sources it compiles, the corpus the tests
# read, and the project file itself. Everything else in a Unity project — Library above all —
# is enormous and irrelevant.
TREES = [
    os.path.join("Assets", "GameAssets", "Core"),
    os.path.join("Assets", "GameAssets", "Tests"),
    os.path.join("Tools", "corpus"),
    os.path.join("Tools", "out"),
    os.path.join("Tools", "dotnet"),
]

TEST_PROJECT = os.path.join("Tools", "dotnet", "RelicRun.Core.Tests")

# Build leftovers would be copied stale and then rebuilt anyway.
IGNORE = shutil.ignore_patterns("bin", "obj", "*.meta", "*.user")


def stage(into):
    """Copies just enough of the project to run the tests, and nothing Unity owns."""
    for tree in TREES:
        src = os.path.join(ROOT, tree)
        if not os.path.isdir(src):
            sys.exit("missing from the project: " + tree)
        shutil.copytree(src, os.path.join(into, tree), ignore=IGNORE)


def run_tests(where):
    """Returns (passed, built)."""
    result = subprocess.run(
        ["dotnet", "test", TEST_PROJECT, "--nologo"],
        cwd=where, capture_output=True, text=True)
    return result.returncode == 0, "error CS" not in result.stdout


def main():
    if len(sys.argv) != 2:
        sys.exit(__doc__)

    with open(sys.argv[1], encoding="utf-8") as handle:
        suite = json.load(handle)

    mutants = suite["mutants"]
    print("mutating " + suite.get("name", sys.argv[1]) + " — " + str(len(mutants)) + " mutants")
    print("(in a copy; the project is not written to)\n")

    staged = tempfile.mkdtemp(prefix="relicrun-mutate-")
    survivors = []

    try:
        stage(staged)

        # A copy that cannot pass its own tests would report every mutant as killed.
        passed, built = run_tests(staged)
        if not passed:
            sys.exit("the unmutated copy does not pass" + ("" if built else " (and did not build)"))

        # Line endings are normalised in the copy, and every write below puts them back the
        # same way. Generated files are written CRLF and hand-written ones LF, so without this
        # a multi-line anchor would match in one file and silently miss in another — which
        # reads exactly like a mutant that could not be applied.
        originals = {}
        for mutant in mutants:
            path = os.path.join(staged, mutant["file"].replace("/", os.sep))
            if path not in originals:
                with open(path, encoding="utf-8", newline="") as handle:
                    originals[path] = handle.read().replace("\r\n", "\n")

        for mutant in mutants:
            name = mutant["name"]
            path = os.path.join(staged, mutant["file"].replace("/", os.sep))

            for other, text in originals.items():
                with open(other, "w", encoding="utf-8", newline="\n") as handle:
                    handle.write(text)

            source = originals[path]
            if mutant["find"] not in source:
                print("  NO ANCHOR   " + name)
                survivors.append(name + " (anchor)")
                continue

            with open(path, "w", encoding="utf-8", newline="\n") as handle:
                handle.write(source.replace(mutant["find"], mutant["replace"], 1))

            passed, built = run_tests(staged)
            if not built:
                print("  NO BUILD    " + name)
                survivors.append(name + " (build)")
            elif passed:
                print("  SURVIVED    " + name)
                survivors.append(name)
            else:
                print("  killed      " + name)
    finally:
        shutil.rmtree(staged, ignore_errors=True)

    print()
    if survivors:
        print(str(len(survivors)) + " of " + str(len(mutants)) + " survived:")
        for name in survivors:
            print("  ! " + name)
    else:
        print("all " + str(len(mutants)) + " mutants killed")

    return 1 if survivors else 0


if __name__ == "__main__":
    sys.exit(main())
