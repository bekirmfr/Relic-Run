/*
 * Shell corpus — the size the game was drawn against.
 *
 *   node Tools/capture/shell.mjs
 *
 * Two numbers, and they are the reason a whole layer of the port has the shape it does. The
 * source draws everything inside one fixed box — `.dd-shell` — and every font size, padding and
 * offset in eight thousand lines of markup is a fraction of it. Port the sizes without the box
 * and the arithmetic is meaningless: 14px is a heading in a 390px shell and a footnote in a
 * 1080px one.
 *
 * They are READ rather than typed, because a number typed into C# from a glance at the markup is
 * a number nobody can check afterwards. PixelScale.DesignWidth and DesignHeight are asserted
 * against this, so the day somebody redraws the shell the port says so instead of quietly
 * scaling everything to a box the game no longer uses.
 *
 * The scale divisor is recorded too, and deliberately NOT used. The source divides the viewport
 * by 848 — the shell's height plus its 2px borders — and takes the fraction, because a browser
 * rasterises outlines afresh at any size. The port cannot: it magnifies a baked bitmap, so it
 * floors to a whole number instead. Recording the number the port does not use is what makes
 * that a decision rather than an oversight.
 *
 * The three layouts' breakpoints are read off the same line of the source, for the same reason.
 * They are four numbers on one line and they decide which of three layouts every screen in the
 * game is drawn in — exactly the sort of thing that is copied correctly, then quietly edited on
 * one side. ViewModes asserts against these.
 */

import { readFileSync, writeFileSync, mkdirSync } from "node:fs";
import { join, dirname } from "node:path";
import { fileURLToPath } from "node:url";

const ROOT = join(dirname(fileURLToPath(import.meta.url)), "..", "..");
const OUT = join(ROOT, "Tools", "corpus");
const SOURCE = join(ROOT, ".port", "Daily Delve.dc.html");

const html = readFileSync(SOURCE, "utf8");

/** The one element the whole game is drawn inside. */
const shell = html.match(/class="dd-shell[^"]*"\s+style="width:(\d+)px;height:(\d+)px/);
if (!shell) throw new Error("no .dd-shell with a fixed width and height — has the source been redrawn?");

/** What the source divides the viewport by before scaling the shell down. */
const divisor = html.match(/--dd-scale",\s*Math\.min\(1,\s*\(window\.innerHeight\s*-\s*(\d+)\)\s*\/\s*(\d+)\)/);
if (!divisor) throw new Error("no --dd-scale calculation — the source no longer scales its shell");

/*
 * The one line that picks a layout. Read whole, so a threshold that moves is a threshold this
 * notices rather than a regular expression matching a number that happens to sit nearby.
 */
const view = html.match(
  /mode = \(w >= (\d+) && h >= (\d+) && w > h\) \? "desktop" : \(w >= (\d+) && w > (\d+)\) \? "tablet" : "phone"/);
if (!view) throw new Error("no view-mode breakpoints — the source no longer picks between three layouts");

const width = Number(shell[1]);
const height = Number(shell[2]);

/*
 * A shell that is not a portrait phone is a shell this port has misread. Cheap, and it is the
 * difference between a corpus that records the game and one that records whatever the regular
 * expression happened to hit first.
 */
if (width < 240 || width > 640) throw new Error("shell width of " + width + " is not a phone");
if (height < width) throw new Error("shell is not portrait: " + width + "x" + height);

const shellData = {
  width,
  height,
  /* The source's own scale: fractional, capped at 1, and only ever shrinking. */
  scale: {
    inset: Number(divisor[1]),
    divisor: Number(divisor[2]),
    capped: 1,
  },
  /*
   * The breakpoints, in the source's CSS pixels. The port measures the same thresholds in canvas
   * units instead — it has no fractional device-pixel-ratio to divide by — which is a difference
   * in what is measured, not in where the lines are drawn.
   *
   * `tabletFloor` is the source's `w > 430`, recorded and not ported: it cannot fail once
   * `w >= 760` has passed. Recorded anyway so the omission is a reading of the line rather than
   * a part of it that was missed.
   */
  view: {
    desktopWide: Number(view[1]),
    desktopTall: Number(view[2]),
    tabletWide: Number(view[3]),
    tabletFloor: Number(view[4]),
  },
};

mkdirSync(OUT, { recursive: true });
const json = JSON.stringify(shellData);
writeFileSync(join(OUT, "shell.json"), json, "utf8");

console.log("\nshell corpus");
console.log("  → shell.json         " + json.length + " bytes");
console.log("  the game is drawn in " + width + "x" + height);
console.log("  and scaled by min(1, (height - " + shellData.scale.inset + ") / " +
            shellData.scale.divisor + "), which the port floors instead");
console.log("  the layouts turn over at " + shellData.view.tabletWide + " and " +
            shellData.view.desktopWide + "x" + shellData.view.desktopTall);
