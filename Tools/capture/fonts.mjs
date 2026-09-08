/*
 * Font corpus — what each face can actually draw.
 *
 *   node Tools/capture/fonts.mjs
 *
 * The port ships two pixel faces and eight languages, and those two facts are in tension: a
 * face designed on a 5x7 grid has a few hundred glyphs in it, and Japanese alone needs four
 * hundred characters. A build where three of the eight languages render as empty boxes is not
 * something to discover from a screenshot.
 *
 * So the fonts' own character maps are recorded here, straight out of the TTF, and nothing
 * else. WHAT a language needs is decided in C# — it is whatever survives Locale.Clean, which
 * is a rule with its own gate — and whether a face can read it is the intersection of the two.
 * Recording the intersection instead would make the answer agree with the question, which is
 * the mistake the palette recorder made once already.
 */

import { readFileSync, writeFileSync, mkdirSync } from "node:fs";
import { join, dirname } from "node:path";
import { fileURLToPath } from "node:url";

const ROOT = join(dirname(fileURLToPath(import.meta.url)), "..", "..");
const OUT = join(ROOT, "Tools", "corpus");
const FONTS = join(ROOT, ".port", "assets");

/* The faces the port ships, and the id each is bound under. */
const FACES = [
  { id: "press-start-2p", file: "PressStart2P.ttf" },
  { id: "jacquard-12", file: "Jacquard12.ttf" },
];

/** Table directory offset for a four-character tag, or 0. */
function tableOf(buffer, tag) {
  const count = buffer.readUInt16BE(4);
  for (let i = 0; i < count; i++) {
    const record = 12 + i * 16;
    if (buffer.toString("ascii", record, record + 4) === tag) return buffer.readUInt32BE(record + 8);
  }

  return 0;
}

/** The face's name table entries, by name id. */
function names(buffer) {
  const table = tableOf(buffer, "name");
  const count = buffer.readUInt16BE(table + 2);
  const strings = table + buffer.readUInt16BE(table + 4);
  const found = {};

  for (let i = 0; i < count; i++) {
    const record = table + 6 + i * 12;
    const platform = buffer.readUInt16BE(record);
    const nameId = buffer.readUInt16BE(record + 6);
    const length = buffer.readUInt16BE(record + 8);
    const offset = buffer.readUInt16BE(record + 10);
    if (found[nameId] != null) continue;

    const raw = buffer.subarray(strings + offset, strings + offset + length);
    found[nameId] = platform === 3 ? Buffer.from(raw).swap16().toString("utf16le") : raw.toString("latin1");
  }

  return found;
}

/**
 * Every code point the face has a glyph for.
 *
 * Formats 4 and 12 only. Between them they cover every font anyone ships this century, and a
 * face using something else would be caught by the check below rather than silently reported
 * as empty.
 */
function coverage(buffer) {
  const cmap = tableOf(buffer, "cmap");
  const tables = buffer.readUInt16BE(cmap + 2);

  let best = 0;
  for (let i = 0; i < tables; i++) {
    const record = cmap + 4 + i * 8;
    const platform = buffer.readUInt16BE(record);
    const encoding = buffer.readUInt16BE(record + 2);

    /* Windows BMP or Windows full-repertoire, else anything Unicode. */
    if ((platform === 3 && (encoding === 1 || encoding === 10)) || platform === 0) {
      best = cmap + buffer.readUInt32BE(record + 4);
    }
  }

  if (!best) throw new Error("no Unicode cmap subtable");

  const format = buffer.readUInt16BE(best);
  const points = new Set();

  if (format === 4) {
    const segments = buffer.readUInt16BE(best + 6) / 2;
    const ends = best + 14;
    const starts = ends + segments * 2 + 2;
    const deltas = starts + segments * 2;
    const ranges = deltas + segments * 2;

    for (let s = 0; s < segments; s++) {
      const end = buffer.readUInt16BE(ends + s * 2);
      const start = buffer.readUInt16BE(starts + s * 2);
      const delta = buffer.readInt16BE(deltas + s * 2);
      const range = buffer.readUInt16BE(ranges + s * 2);
      if (start === 0xffff) continue;

      for (let c = start; c <= end; c++) {
        let glyph;
        if (range === 0) glyph = (c + delta) & 0xffff;
        else {
          const at = ranges + s * 2 + range + (c - start) * 2;
          if (at + 1 >= buffer.length) continue;
          glyph = buffer.readUInt16BE(at);
          if (glyph !== 0) glyph = (glyph + delta) & 0xffff;
        }

        if (glyph !== 0) points.add(c);
      }
    }
  } else if (format === 12) {
    const groups = buffer.readUInt32BE(best + 12);
    for (let g = 0; g < groups; g++) {
      const record = best + 16 + g * 12;
      const start = buffer.readUInt32BE(record);
      const end = buffer.readUInt32BE(record + 4);
      for (let c = start; c <= end; c++) points.add(c);
    }
  } else {
    throw new Error("cmap format " + format + " is not read here");
  }

  return [...points].sort((a, b) => a - b);
}

const faces = FACES.map((face) => {
  const buffer = readFileSync(join(FONTS, face.file));
  const named = names(buffer);
  const head = tableOf(buffer, "head");

  return {
    id: face.id,
    file: face.file,
    family: named[1],
    subfamily: named[2],
    version: named[5],
    unitsPerEm: buffer.readUInt16BE(head + 18),
    covers: coverage(buffer),
  };
});

for (const face of faces) {
  if (face.covers.length < 64) throw new Error(face.file + " reads as nearly empty — check the cmap");
}

mkdirSync(OUT, { recursive: true });
const json = JSON.stringify({ faces });
writeFileSync(join(OUT, "fonts.json"), json, "utf8");

console.log("\nfont corpus");
console.log("  → fonts.json         " + faces.length + " faces  " +
            (Buffer.byteLength(json) / 1024).toFixed(0) + " KB");
for (const face of faces) {
  console.log("  " + face.family.padEnd(16) + String(face.covers.length).padStart(5) +
              " glyphs · " + face.unitsPerEm + " units/em");
}
