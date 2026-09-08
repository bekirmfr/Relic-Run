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

# How long one mutant's suite may take before it is called hung. The clean suite runs in about
# twenty seconds, so this is generous by an order of magnitude and still catches a loop that
# will never end.
DEADLINE = 300

# Build leftovers would be copied stale and then rebuilt anyway.
IGNORE = shutil.ignore_patterns("bin", "obj", "*.meta", "*.user")


# Single files the tests read that are not worth copying a whole tree for. The hero pack is the
# shipped art, half a megabyte of it, and it sits in a directory of PNGs almost none of which
# these tests open. The two that are here are opened for their headers alone: they are the only
# witnesses in the project to how big a sprite sheet actually is.
FILES = [
    os.path.join(".port", "hero-pack.json"),
    os.path.join(".port", "assets", "relic-icons.png"),
    os.path.join(".port", "assets", "enemies-hoard.png"),
]


def stage(into):
    """Copies just enough of the project to run the tests, and nothing Unity owns."""
    for tree in TREES:
        src = os.path.join(ROOT, tree)
        if not os.path.isdir(src):
            sys.exit("missing from the project: " + tree)
        shutil.copytree(src, os.path.join(into, tree), ignore=IGNORE)

    for name in FILES:
        src = os.path.join(ROOT, name)
        if not os.path.isfile(src):
            sys.exit("missing from the project: " + name)
        dst = os.path.join(into, name)
        os.makedirs(os.path.dirname(dst), exist_ok=True)
        shutil.copyfile(src, dst)


def run_tests(where):
    """Returns (passed, built)."""
    # Decoded as UTF-8 rather than as the console's own codepage. A failing test prints the
    # strings it compared, and the game speaks Arabic, Japanese and Russian — on a Windows
    # console that is cp1252, and reading it as such kills the reader thread mid-suite.
    #
    # And a deadline, because a mutant can hang rather than fail. A cursor that stops advancing
    # turns any `while not finished` loop into a forever, and a suite that never returns wedges
    # the harness with no output at all — which is how this one sat for an hour looking like a
    # slow machine. A mutant that hangs is a mutant that was NOT killed, so a timeout counts as
    # a survivor and says so.
    #
    # And the tree, not the child. `dotnet test` spawns a test host, and killing only the
    # process we launched leaves that host spinning on the very loop that caused the timeout —
    # one pegged core per hung mutant, quietly making every later mutant slower. That is how a
    # single hang turned into an afternoon of a machine that seemed inexplicably tired.
    running = subprocess.Popen(
        ["dotnet", "test", TEST_PROJECT, "--nologo"],
        cwd=where, stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True,
        encoding="utf-8", errors="replace")

    try:
        out, _ = running.communicate(timeout=DEADLINE)
    except subprocess.TimeoutExpired:
        reap(running.pid)
        running.communicate()
        return True, True

    return running.returncode == 0, "error CS" not in out


def reap(pid):
    """Kills a process and everything it started."""
    if os.name == "nt":
        subprocess.run(["taskkill", "/F", "/T", "/PID", str(pid)],
                       capture_output=True, check=False)
        return

    try:
        os.killpg(os.getpgid(pid), 9)
    except OSError:
        pass


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
