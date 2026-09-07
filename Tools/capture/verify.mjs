/*
 * Corpus self-check.
 *
 *   node Tools/capture/verify.mjs
 *
 * Replays every recorded case from its recorded input alone — the same thing
 * RelicRun.Core will do — and diffs the resulting event stream against what was
 * stored. This proves the corpus is a sufficient contract: if any case needs
 * state we did not capture, it fails HERE, while the cause is still obviously the
 * recording and not a C# port bug.
 */

import { readFileSync, readdirSync } from "node:fs";
import { execFileSync } from "node:child_process";
import { join, dirname } from "node:path";
import { fileURLToPath } from "node:url";
import { buildEngine } from "./engine.mjs";

const ROOT = join(dirname(fileURLToPath(import.meta.url)), "..", "..");
const DIR = join(ROOT, "Tools", "corpus");
const api = buildEngine();
const engine = new api.Engine();
const clone = (o) => JSON.parse(JSON.stringify(o));
const load = (f) => JSON.parse(readFileSync(join(DIR, f), "utf8"));

let checked = 0, failed = 0;
const failures = [];

/** Field-by-field diff of two event streams; returns the first divergence. */
function diffEvents(want, got, label) {
  if (want.length !== got.length) return `${label}: ${want.length} events recorded, ${got.length} replayed`;
  for (let i = 0; i < want.length; i++) {
    const a = want[i], b = got[i];
    const keys = new Set([...Object.keys(a), ...Object.keys(b)]);
    for (const k of keys) {
      const av = a[k], bv = b[k];
      if (JSON.stringify(av) !== JSON.stringify(bv)) {
        return `${label}: event ${i} (${a.t}) field "${k}" — recorded ${JSON.stringify(av)}, replayed ${JSON.stringify(bv)}`;
      }
    }
  }
  return null;
}

function fail(msg) { failed++; if (failures.length < 10) failures.push(msg); }

/* ---------- rng ---------- */

console.log("\nrng");
for (const c of load("rng.json")) {
  const r = api.mulberry32(c.seed);
  const got = Array.from({ length: c.raw.length }, () => r() * 4294967296);
  const bad = c.raw.findIndex((v, i) => v !== got[i]);
  checked++;
  if (bad >= 0) fail(`rng seed ${c.seed}: draw ${bad} diverges`);
}
console.log(`  ${load("rng.json").length} seeds x 1000 raw draws`);

/* ---------- packs ---------- */

console.log("\npacks");
for (const p of load("packs.json")) {
  const cfg = api.DUNGEONS[p.dungeon - 1];
  const got = api.packFor(p.floor, api.mulberry32(p.seed), {
    bossRelics: cfg.bossRelics, ghoolemBoss: !!cfg.ghoolemBoss,
  });
  checked++;
  if (JSON.stringify(got) !== JSON.stringify(p.pack)) {
    fail(`pack d${p.dungeon}/f${p.floor}/seed ${p.seed} diverges`);
  }
}
console.log(`  ${load("packs.json").length} packs`);

/* ---------- fights ---------- */

/* Fight tiers are the corpus files holding an ARRAY of replayable cases. Detect that by
   shape rather than by maintaining a list of exclusions - a new non-fight corpus file used to
   crash this script, and adding its name here would only defer the next one.
   A fight case is one with a recorded INPUT and an ARRAY of events; delve.json has neither,
   which is what the run corpus below checks instead. */
const tiers = readdirSync(DIR)
  .filter((f) => f.endsWith(".json") && f !== "manifest.json")
  .filter((f) => {
    const data = load(f);
    return Array.isArray(data) && data.length > 0 && data[0] &&
           Array.isArray(data[0].events) && data[0].input !== undefined;
  })
  .map((f) => f.replace(/\.json$/, ""));

for (const tier of tiers) {
  const list = load(`${tier}.json`);
  let bad = 0;
  for (const c of list) {
    checked++;
    let got;
    if (c.mode === "versus") {
      got = engine.simulateDuel(clone(c.input.a), clone(c.input.b), api.mulberry32(c.fightSeed));
    } else {
      got = engine.simulateFloor(clone(c.input.hero), clone(c.input.pack), api.mulberry32(c.fightSeed));
    }
    const d = diffEvents(c.events, [...got], c.id);
    if (d) { fail(d); bad++; }
  }
  console.log(`\n${tier}\n  ${list.length} cases${bad ? `, ${bad} FAILED` : ""}`);
}

/* ---------- the run corpus ---------- */

/* Runs and matches are not replayed from a recorded input the way a fight is — the recorder
   drives the game's own UI from a seed, so regenerating IS the replay. What has to hold is that
   it regenerates identically: same seeds, same events, same decisions, same states. If the
   lifter ever picks up a different slice of the source, or a hook stops being read-only, this
   is where it shows. */
for (const [name, script, unit] of [["delve", "delve.mjs", "runs"], ["versus", "versus.mjs", "matches"]]) {
  const file = `${name}.json`;
  const recorded = readFileSync(join(DIR, file), "utf8");
  execFileSync(process.execPath, [join(ROOT, "Tools", "capture", script)],
    { encoding: "utf8", stdio: ["ignore", "pipe", "inherit"] });
  const now = readFileSync(join(DIR, file), "utf8");
  const list = JSON.parse(now);

  checked += list.length;
  if (recorded !== now) {
    fail(`${file} is not reproducible — regenerating it produced different bytes`);
    failed += list.length;
    console.log(`
${name}
  ${list.length} ${unit}, NOT REPRODUCIBLE`);
  } else {
    const steps = list.reduce((n, r) => n + (r.steps || r.rounds || []).length, 0);
    console.log(`
${name}
  ${list.length} ${unit}, ${steps} steps`);
  }
}

/* ---------- report ---------- */

if (failures.length) {
  console.log("\nfirst divergences");
  for (const f of failures) console.log(`  ! ${f}`);
}
console.log(failed === 0
  ? `\nCORPUS VERIFIED — ${checked} cases replay exactly from their recorded input\n`
  : `\nCORPUS BROKEN — ${failed}/${checked} cases diverge\n`);
process.exit(failed === 0 ? 0 : 1);
