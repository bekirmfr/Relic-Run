/*
 * Synergy corpus — how the game reads a build.
 *
 *   node Tools/capture/synergy.mjs
 *
 * relicSynergy scores how well a relic fits a loadout, and it is shipped rather than harness:
 * the rival delvers in versus draft with it between rounds. It is small enough to port by
 * reading and subtle enough to get wrong — set tiers, the dominant kind, and a chain graph of
 * what the loadout produces against what it consumes — so it gets a corpus of its own.
 *
 * Scores land on halves, so they are recorded DOUBLED. That keeps the file free of float
 * literals, which is the rule everywhere else in the corpus and the reason none of it has ever
 * diverged on a parsed decimal.
 */

import { writeFileSync, mkdirSync } from "node:fs";
import { join, dirname } from "node:path";
import { fileURLToPath } from "node:url";
import { buildEngine } from "./engine.mjs";

const ROOT = join(dirname(fileURLToPath(import.meta.url)), "..", "..");
const OUT = join(ROOT, "Tools", "corpus");

const api = buildEngine();
const rng = api.mulberry32(0x5A11AD);
const pool = api.POOL;

const cases = [];

function record(id, owned) {
  const score = api.relicSynergy(id, owned);
  const doubled = Math.round(score * 2);
  if (Math.abs(doubled / 2 - score) > 1e-9) {
    throw new Error(`synergy score ${score} for ${id} is not a multiple of a half`);
  }

  cases.push({ id, owned: owned.slice(), score2: doubled });
}

// Every relic against an empty build: the baseline, and where a dead trigger shows.
for (const id of pool) record(id, []);

// Every relic against a build of one, which is where the chain graph starts to bite.
for (const id of pool) {
  for (const other of ["clover", "tooth", "alchemist", "thorns", "cutpurse", "dice", "magnet"]) {
    record(id, [other]);
  }
}

// Set tiers, and the sizes either side of them, so the pick that opens a tier is scored
// against the picks that do not. Five and eight matter for a different reason: riding an
// established kind is CAPPED, and the cap only bites once a kind is five deep.
const byKind = {};
for (const id of pool) {
  const kind = api.ITEMS[id].kind;
  (byKind[kind] || (byKind[kind] = [])).push(id);
}

for (const kind of Object.keys(byKind)) {
  const ids = byKind[kind];
  for (const n of [1, 2, 3, 4, 5, 6, 7, 8]) {
    const owned = [];
    while (owned.length < n) owned.push(ids[owned.length % ids.length]);
    for (const id of ids) record(id, owned);
  }
}

// And a spread of random builds, so combinations nobody thought to design get scored too.
for (let i = 0; i < 400; i++) {
  const owned = [];
  const n = 1 + Math.floor(rng() * 8);
  while (owned.length < n) owned.push(pool[Math.floor(rng() * pool.length)]);
  record(pool[Math.floor(rng() * pool.length)], owned);
}

mkdirSync(OUT, { recursive: true });
const json = JSON.stringify(cases);
writeFileSync(join(OUT, "synergy.json"), json, "utf8");

const spread = {};
for (const c of cases) spread[c.score2] = (spread[c.score2] || 0) + 1;
console.log("\nsynergy corpus");
console.log(`  → synergy.json       ${String(cases.length).padStart(4)} cases  ` +
            `${(Buffer.byteLength(json) / 1024).toFixed(0)} KB`);
console.log(`  ${Object.keys(spread).length} distinct scores, ` +
            `${Math.min(...cases.map((c) => c.score2)) / 2} to ${Math.max(...cases.map((c) => c.score2)) / 2}`);
