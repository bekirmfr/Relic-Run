# Testing

Two runners, **one set of source files**. `RelicRun.Core` sets `noEngineReferences`, so it is
ordinary C# and needs no Unity to run — which is what makes the fast loop possible.

## Fast loop — `dotnet test`

```bash
dotnet test Tools/dotnet/RelicRun.Core.Tests
```

Runs in about a second, needs no editor, works in CI, and can be run by an agent. This is the
loop that verifies each phase against the golden corpus while it is being written.

`Tools/dotnet/RelicRun.Core.Tests.csproj` **compiles** the Unity sources by glob — it never copies
them:

```xml
<Compile Include="../../../Assets/GameAssets/Core/**/*.cs" />
<Compile Include="../../../Assets/GameAssets/Tests/**/*.cs" />
```

## Parity check — Unity Test Runner

In the editor: **Window → General → Test Runner → EditMode → Run All**.

Headless (close the editor first — Unity will not take the project lock twice):

```bash
"C:/Program Files/Unity/Hub/Editor/6000.0.68f1/Editor/Unity.exe" -runTests -batchmode -projectPath . -testPlatform EditMode -testResults Logs/tests.xml
```

Run this at phase boundaries. It confirms the same code compiles and passes inside Unity, and it
is the only way to test the `Game` and `Editor` assemblies once those exist.

## Two rules that keep the arrangement honest

**C# 9 only.** The csproj pins `<LangVersion>9.0</LangVersion>` because that is what Unity 6
compiles. Without the pin, `dotnet` would accept C# 13 and let newer syntax through, and the
failure would surface in Unity much later and far from its cause. If `dotnet test` rejects your
syntax, Unity would have too.

**Tests stay Unity-free.** Everything under `Assets/GameAssets/Tests/` is compiled by both
runners, so it must not reference `UnityEngine`. Tests that genuinely need Unity belong in a
separate assembly, added when the `Game` layer arrives.

**Corpus values are integers.** Never record a float as the expected value of a gate. Unity's
JSON parser is not correctly rounded: it read `0.1853655439335853` back one ULP high, failing the
RNG gate against a port that was bit-perfect, while .NET 9's correctly-rounded parser passed and
hid it. The generator is recorded as its raw 32-bit output instead — a draw is exactly
`raw / 2^32`, so the integer is lossless and every parser agrees. All 446,999 numbers across the
corpus are now integers; keep it that way when adding cases.

JSON goes through Newtonsoft on both sides — Unity supplies it as
`com.unity.nuget.newtonsoft-json`, the csproj references the identical NuGet package — so no
conditional compilation is needed anywhere.

## The corpus

Tests read `Tools/corpus/`. If it is missing or you have changed the JS source:

```bash
node Tools/capture/capture.mjs    # regenerate (deterministic, fixed master seed)
node Tools/capture/verify.mjs     # prove every case replays from its recorded input
```

Content JSON is separate, and has its own gate:

```bash
node Tools/extract/extract.mjs
node Tools/extract/gen-csharp.mjs   # regenerate Core/Content/*.cs
node Tools/extract/validate.mjs
```

## Current gates

| Gate | Corpus | Status |
|---|---|---|
| Phase 0 — content | `Tools/out/` | passing, 4 tracked content gaps |
| Phase 0 — corpus | `Tools/corpus/` | passing, 783 cases replay exactly |
| Phase 1 — RNG | `rng.json` | passing, 5 seeds × 1000 raw draws bit-exact |
| Phase 1 — defence | `defense.json` | passing, 1800 grid cells |
| Phase 1 — stat ledger | `statledger.json` | passing, 566 contexts x 4 stats, 5033 labelled rows |
