/*
 * Source lifter — pulls named declarations out of .port/Daily Delve.dc.html so the
 * combat engine can be run headlessly in Node.
 *
 * We slice by name rather than evaluating the whole module scope, because that scope
 * also contains the hero-art rig and Studio code, which touch document/canvas and
 * would need a DOM. The engine itself needs none of that.
 */

import { readFileSync } from "node:fs";
import { join, dirname } from "node:path";
import { fileURLToPath } from "node:url";

const ROOT = join(dirname(fileURLToPath(import.meta.url)), "..", "..");
export const SRC = readFileSync(join(ROOT, ".port", "Daily Delve.dc.html"), "utf8");

/** Walk source from `i`, tracking depth and skipping strings/comments/regex-free code. */
function scan(src, i, stopAtDepthZeroSemicolon) {
  let depth = 0, inStr = null, esc = false;
  for (let k = i; k < src.length; k++) {
    const c = src[k];
    if (esc) { esc = false; continue; }
    if (inStr) {
      if (c === "\\") esc = true;
      else if (c === inStr) inStr = null;
      continue;
    }
    if (c === '"' || c === "'" || c === "`") { inStr = c; continue; }
    if (c === "/" && src[k + 1] === "*") { k = src.indexOf("*/", k + 2) + 1; continue; }
    if (c === "/" && src[k + 1] === "/") { k = src.indexOf("\n", k); continue; }
    if (c === "{" || c === "(" || c === "[") depth++;
    else if (c === "}" || c === ")" || c === "]") {
      depth--;
      if (!stopAtDepthZeroSemicolon && depth === 0) return k + 1;
    } else if (stopAtDepthZeroSemicolon && c === ";" && depth === 0) return k + 1;
  }
  throw new Error("unterminated declaration");
}

/** Source text of a top-level `const/let/var NAME = ...;` statement (whole statement). */
export function liftConst(name) {
  const m = new RegExp(`^(?:const|let|var) ${name}\\b`, "m").exec(SRC);
  if (!m) throw new Error(`const not found: ${name}`);
  return SRC.slice(m.index, scan(SRC, m.index, true));
}

/** Source text of a top-level `function NAME(...) { ... }`. */
export function liftFunction(name) {
  const m = new RegExp(`^function ${name}\\s*\\(`, "m").exec(SRC);
  if (!m) throw new Error(`function not found: ${name}`);
  const bodyStart = SRC.indexOf("{", m.index);
  return SRC.slice(m.index, scan(SRC, bodyStart, false));
}

/** Source text of a method on the Component class, indented by two spaces. */
export function liftMethod(name) {
  const m = new RegExp(`^  ${name}\\s*\\(`, "m").exec(SRC);
  if (!m) throw new Error(`method not found: ${name}`);
  const bodyStart = SRC.indexOf("{", m.index);
  return SRC.slice(m.index, scan(SRC, bodyStart, false));
}

/** An IIFE-backed const such as Store or SFX, with its initializer intact. */
export const liftIife = liftConst;
