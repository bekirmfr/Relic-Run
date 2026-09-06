/*
 * Relic Run content validator — the Phase 0 gate.
 *
 * Asserts the structural invariants the Unity importer and the Core engine will
 * rely on. Exits non-zero if any CHECK fails.
 *
 * GAPS are different: real, known content holes in the source game. They are
 * reported every run so they stay visible, but they do not fail the gate — they
 * are tracked work, not extraction bugs.
 *
 *   node Tools/extract/validate.mjs
 */

import { readFileSync, readdirSync } from "node:fs";
import { join, dirname } from "node:path";
import { fileURLToPath } from "node:url";

const ROOT = join(dirname(fileURLToPath(import.meta.url)), "..", "..");
const OUT = join(ROOT, "Tools", "out");
const load = (f) => JSON.parse(readFileSync(join(OUT, f), "utf8"));

let failures = 0;
const gaps = [];

function check(label, cond, detail) {
  if (cond) { console.log(`  ok   ${label}`); return; }
  failures++;
  console.log(`  FAIL ${label}${detail ? " — " + detail : ""}`);
}
const gap = (label, detail) => gaps.push({ label, detail });

/* ---------- load ---------- */

const relics = load("relics.json");
const cut = load("relics-cut.json");
const kinds = load("kinds.json");
const sockets = load("sockets.json");
const dungeons = load("dungeons.json");
const enemies = load("enemies.json");
const events = load("events.json");
const progression = load("progression.json");
const constants = load("constants.json");
const manifest = load("manifest.json");
const localeIds = readdirSync(join(OUT, "locales")).map((f) => f.replace(/\.json$/, ""));
const locales = Object.fromEntries(localeIds.map((id) => [id, load(`locales/${id}.json`)]));

/* ---------- relics ---------- */

console.log("\nrelics");
const relicIds = new Set(relics.map((r) => r.id));
check("50 relics", relics.length === 50, `got ${relics.length}`);
check("ids unique", relicIds.size === relics.length);
check("no overlap with the cut set", !cut.some((c) => relicIds.has(c.id)));

const kindNames = new Set(Object.keys(kinds));
const badKind = relics.filter((r) => !kindNames.has(r.kind));
check("every kind is defined", badKind.length === 0, badKind.map((r) => r.id).join(", "));

const CHANNELS = new Set(["dmg", "heal", "gold", "luck", "hit", "pain", "kill", "crit", "dodge"]);
const badCh = relics.filter((r) => [...r.emits, ...r.reacts].some((c) => !CHANNELS.has(c)));
check("emits/reacts use known channels", badCh.length === 0, badCh.map((r) => r.id).join(", "));

const MODES = new Set(["delve", "versus"]);
check("modes are valid", relics.every((r) => r.modes.length && r.modes.every((m) => MODES.has(m))));

/* native triggers name a BUS channel, which is a wider set than the socket catalogue */
const busNames = new Set(sockets.busTriggers);
const badTrig = relics.filter((r) => r.nativeTrigger && !busNames.has(r.nativeTrigger));
check("native triggers resolve to a bus channel", badTrig.length === 0, badTrig.map((r) => r.id).join(", "));

check("every relic has text", relics.every((r) => r.text.name && r.text.desc));

/* icon cells must be inside the 10x8 sheet and unique */
const withIcon = relics.filter((r) => r.icon);
const oob = withIcon.filter((r) => r.icon.col < 0 || r.icon.col > 9 || r.icon.row < 0 || r.icon.row > 7);
check("icon cells inside the 10x8 sheet", oob.length === 0, oob.map((r) => r.id).join(", "));
const cellKeys = withIcon.map((r) => `${r.icon.col},${r.icon.row}`);
check("icon cells unique", new Set(cellKeys).size === cellKeys.length);

const noIcon = relics.filter((r) => !r.icon);
if (noIcon.length) {
  gap("relics without an icon cell",
    `${noIcon.map((r) => r.id).join(", ")} — falls back to the legacy procedural glyph, ` +
    `which the port drops. Needs a cell in assets/relic-icons.png (33 of 80 free).`);
}

/* ---------- kinds ---------- */

console.log("\nkinds");
check("8 kinds", Object.keys(kinds).length === 8);
check("every kind has a colour", Object.values(kinds).every((k) => /^#[0-9A-Fa-f]{6}$/.test(k.c)));
const perKind = {};
for (const r of relics) perKind[r.kind] = (perKind[r.kind] || 0) + 1;
const thin = Object.entries(kinds)
  .map(([k, v]) => [k, Math.max(...Object.keys(v.tiers).map(Number)), perKind[k] || 0])
  .filter(([, top, have]) => top > have);
if (thin.length) {
  gap("set tiers above the pool size",
    thin.map(([k, top, have]) => `${k} ${have}/${top}`).join(" · ") +
    " — reachable only through duplicate copies or Hollow Idol, never from distinct relics.");
}

/* ---------- sockets ---------- */

console.log("\nsockets");
check("8 triggers", Object.keys(sockets.triggers).length === 8);
check("7 emitters", Object.keys(sockets.emitters).length === 7);
check("every emitter has an amount", Object.values(sockets.emitters).every((e) => typeof e.amt === "number"));
check("socket catalogue is a subset of the bus", Object.keys(sockets.triggers).every((t) => sockets.busTriggers.includes(t)));
for (const t of sockets.unsocketableBusTriggers) {
  gap(`bus channel ${t} has no purchasable trigger`,
    `the engine calls fireTrig("${t}", …) but no TRIGGER exposes it, so no socket can ever ` +
    `hold it and the call is a permanent no-op. Port decision needed: add the socket, or drop the call.`);
}

/* ---------- dungeons ---------- */

console.log("\ndungeons");
check("10 dungeons", dungeons.length === 10);
check("ids are 1..10 in order", dungeons.every((d, i) => d.id === i + 1));
check("recommended levels ascend by 3", dungeons.every((d, i) => d.lvl === 1 + i * 3));
check("multipliers ascend", dungeons.every((d, i) => i === 0 || d.mult > dungeons[i - 1].mult));
const badBoss = dungeons.flatMap((d) => (d.bossRelics || []).filter((r) => !relicIds.has(r)).map((r) => `${d.id}:${r}`));
check("bossRelics reference live relics", badBoss.length === 0, badBoss.join(", "));
check("every dungeon has hall art", dungeons.every((d) => typeof d.art === "string" && d.art.endsWith(".png")));

/* ---------- enemies ---------- */

console.log("\nenemies");
check("13 species", enemies.species.length === 13);
const rows = enemies.species.map((s) => s.sheetRow).sort((a, b) => a - b);
check("sheet rows are a permutation of 0..12", rows.every((r, i) => r === i));
check("ladder has 11 combat floors", enemies.ladder && enemies.ladder.length === 11);
check("ladder indices are valid species", enemies.ladder.every((i) => i >= 0 && i < 13));
check("ladder excludes the king", !enemies.ladder.includes(enemies.kingSpecies));
check("spare is not on the ladder", !enemies.ladder.includes(enemies.spare));

/* ---------- events ---------- */

console.log("\nevents");
check("12 events", events.length === 12);
check("every event has art", events.every((e) => typeof e.art === "string"));
check("every choice has a label", events.every((e) => e.choices.length && e.choices.every((c) => c.label)));
check("every choice has an outcome closure", events.every((e) => e.choices.every((c) => c.run && c.run.__fn)));
const costed = events.flatMap((e) => e.choices).filter((c) => c.cost != null);
check("costed choices carry a hint", costed.every((c) => c.hint));

/* ---------- progression ---------- */

console.log("\nprogression");
check("xp steps descend", progression.xpSteps.every((v, i) => i === 0 || v < progression.xpSteps[i - 1]));
check("xp floor is below the last step", progression.xpFloor < progression.xpSteps.at(-1));
check("14 achievements", progression.achievements.length === 14);
check("achievement ids unique", new Set(progression.achievements.map((a) => a.id)).size === progression.achievements.length);
check("structural perks keyed by level", Object.keys(progression.structuralPerks).every((k) => Number(k) >= 1));

/* ---------- constants ---------- */

console.log("\nconstants");
check("13 floors", constants.MAX_FLOOR === 13);
check("bazaar sits inside the run", constants.SHOP_FLOOR > 1 && constants.SHOP_FLOOR < constants.MAX_FLOOR);
check("chain cap positive", constants.CHAIN_CAP > 0);
check("defence K is 20", constants.defenseK === 20);

/* ---------- locales ---------- */

console.log("\nlocales");
check("8 locales", localeIds.length === 8, localeIds.join(", "));
check("en is present", localeIds.includes("en"));
const enKeys = Object.keys(locales.en);
for (const id of localeIds.filter((i) => i !== "en")) {
  const missing = enKeys.filter((k) => locales[id][k] == null);
  check(`${id} covers every en key`, missing.length === 0, `${missing.length} missing`);
}
const localized = relics.filter((r) => locales.en[`it_${r.id}_n`] != null);
if (localized.length < relics.length) {
  gap("relics with no translations",
    `${relics.length - localized.length} of ${relics.length} relics are English-only ` +
    `(the expansion set lives in NEWR, not strings.js). Blocks a fully localized Phase 10.`);
}

/* ---------- manifest ---------- */

console.log("\nmanifest");
check("manifest counts match", manifest.counts.relics === relics.length && manifest.counts.events === events.length);

/* ---------- report ---------- */

if (gaps.length) {
  console.log("\ncontent gaps (tracked, not failures)");
  for (const g of gaps) console.log(`  ~ ${g.label}\n      ${g.detail}`);
}

console.log(failures === 0
  ? `\nPHASE 0 GATE: PASS — ${gaps.length} tracked gap(s)\n`
  : `\nPHASE 0 GATE: FAIL — ${failures} check(s) failed\n`);
process.exit(failures === 0 ? 0 : 1);
