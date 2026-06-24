// Generates the PWA icon set as real PNGs — no npm install, just Node's built-in zlib.
// Draws the Roamint "coin pin" mark: a map pin whose head is a reeded coin, on an
// indigo rounded-square badge. Pin uses a teal→mint gradient; the coin face is a
// pale-mint radial with a milled (reeded) edge and a "$" monogram. Anti-aliased via
// 4×4 supersampling. Re-run after changing the palette: `node scripts/gen-pwa-icons.mjs`.
//
// Output -> src/Bootstrap/Roamint.Web/wwwroot/icons/
//   icon-192.png, icon-512.png, icon-maskable-512.png, apple-touch-icon-180.png, favicon-32.png
import { deflateSync } from 'node:zlib';
import { writeFileSync, mkdirSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { dirname, join } from 'node:path';

const OUT = join(dirname(fileURLToPath(import.meta.url)), '..', 'src', 'Bootstrap', 'Roamint.Web', 'wwwroot', 'icons');
mkdirSync(OUT, { recursive: true });

// --- palette (matches the approved mockup) ---
const BADGE_TL = [0x2c, 0x27, 0x70]; // indigo, top-left
const BADGE_BR = [0x16, 0x12, 0x3c]; // deep indigo, bottom-right
const PIN_TOP = [0x34, 0xd7, 0xc0]; // teal
const PIN_MID = [0x14, 0xb8, 0xa6]; // mint-teal
const PIN_BOT = [0x0e, 0x8c, 0x6e]; // deep teal
const COIN_HI = [0xff, 0xff, 0xff]; // coin face centre / highlight
const COIN_LO = [0xbb, 0xf7, 0xd0]; // coin face edge
const REED = [0x9f, 0xf0, 0xd2]; // milled-edge ticks
const RING = [0x86, 0xe5, 0xb6]; // inner detail ring
const GLYPH = [0x21, 0x1d, 0x5c]; // "$" — badge indigo

// 5x7 "$" bitmap — 1 = ink. Central bar runs the full height through an S.
const DOLLAR = [
  [0, 0, 1, 0, 0],
  [0, 1, 1, 1, 0],
  [1, 0, 1, 0, 0],
  [0, 1, 1, 0, 0],
  [0, 0, 1, 0, 1],
  [0, 1, 1, 1, 0],
  [0, 0, 1, 0, 0],
];

// --- minimal PNG encoder (RGBA, filter 0 per scanline) ---
const crcTable = (() => {
  const t = new Uint32Array(256);
  for (let n = 0; n < 256; n++) {
    let c = n;
    for (let k = 0; k < 8; k++) c = c & 1 ? 0xedb88320 ^ (c >>> 1) : c >>> 1;
    t[n] = c >>> 0;
  }
  return t;
})();
function crc32(buf) {
  let c = 0xffffffff;
  for (let i = 0; i < buf.length; i++) c = crcTable[(c ^ buf[i]) & 0xff] ^ (c >>> 8);
  return (c ^ 0xffffffff) >>> 0;
}
function chunk(type, data) {
  const len = Buffer.alloc(4);
  len.writeUInt32BE(data.length, 0);
  const typeBuf = Buffer.from(type, 'ascii');
  const body = Buffer.concat([typeBuf, data]);
  const crc = Buffer.alloc(4);
  crc.writeUInt32BE(crc32(body), 0);
  return Buffer.concat([len, body, crc]);
}
function encodePng(size, pixels) {
  const ihdr = Buffer.alloc(13);
  ihdr.writeUInt32BE(size, 0);
  ihdr.writeUInt32BE(size, 4);
  ihdr[8] = 8;  // bit depth
  ihdr[9] = 6;  // color type RGBA
  const raw = Buffer.alloc(size * (size * 4 + 1));
  for (let y = 0; y < size; y++) {
    raw[y * (size * 4 + 1)] = 0; // filter: none
    for (let x = 0; x < size; x++) {
      const src = (y * size + x) * 4;
      const dst = y * (size * 4 + 1) + 1 + x * 4;
      raw[dst] = pixels[src];
      raw[dst + 1] = pixels[src + 1];
      raw[dst + 2] = pixels[src + 2];
      raw[dst + 3] = pixels[src + 3];
    }
  }
  return Buffer.concat([
    Buffer.from([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a]),
    chunk('IHDR', ihdr),
    chunk('IDAT', deflateSync(raw, { level: 9 })),
    chunk('IEND', Buffer.alloc(0)),
  ]);
}

const lerp = (a, b, t) => [
  Math.round(a[0] + (b[0] - a[0]) * t),
  Math.round(a[1] + (b[1] - a[1]) * t),
  Math.round(a[2] + (b[2] - a[2]) * t),
];
const clamp01 = (t) => (t < 0 ? 0 : t > 1 ? 1 : t);

// Colour of a single sub-sample at (sx, sy). Returns [r,g,b,a] (a is 0 or 255).
// `g` carries the precomputed mark geometry for this target.
function sampleColor(sx, sy, size, opts, g) {
  // 1. background extent — rounded square (transparent corners) or full bleed.
  if (!opts.bleed) {
    const inset = opts.padding * size;
    if (sx < inset || sx >= size - inset || sy < inset || sy >= size - inset) {
      return [0, 0, 0, 0];
    }
    const lx = sx - inset, ly = sy - inset, side = size - 2 * inset;
    const radius = side * 0.22;
    const cornerX = Math.min(lx, side - lx), cornerY = Math.min(ly, side - ly);
    if (cornerX < radius && cornerY < radius) {
      const dx = radius - cornerX, dy = radius - cornerY;
      if (dx * dx + dy * dy > radius * radius) return [0, 0, 0, 0];
    }
  }

  // 2. badge gradient (top-left → bottom-right).
  let col = lerp(BADGE_TL, BADGE_BR, clamp01((sx + sy) / (2 * size)));

  // 3. pin silhouette: head circle ∪ tangent cone down to the tip.
  const dHead = Math.hypot(sx - g.cx, sy - g.headCy);
  const inHead = dHead <= g.r;
  let inPin = inHead;
  if (!inPin) {
    // point-in-triangle(tip, PL, PR) via consistent cross-product signs.
    const s = (ax, ay, bx, by, px, py) => (bx - ax) * (py - ay) - (by - ay) * (px - ax);
    const d1 = s(g.tipX, g.tipY, g.plX, g.plY, sx, sy);
    const d2 = s(g.plX, g.plY, g.prX, g.prY, sx, sy);
    const d3 = s(g.prX, g.prY, g.tipX, g.tipY, sx, sy);
    const hasNeg = d1 < 0 || d2 < 0 || d3 < 0;
    const hasPos = d1 > 0 || d2 > 0 || d3 > 0;
    inPin = !(hasNeg && hasPos);
  }
  if (inPin) {
    const vt = clamp01((sy - (g.headCy - g.r)) / (g.tipY - (g.headCy - g.r)));
    col = vt < 0.55 ? lerp(PIN_TOP, PIN_MID, vt / 0.55) : lerp(PIN_MID, PIN_BOT, (vt - 0.55) / 0.45);
    // soft highlight down the left flank for a touch of dimension.
    if (sx < g.cx) {
      const h = clamp01((g.cx - sx) / g.r) * 0.16;
      col = lerp(col, COIN_HI, h);
    }
  }

  // 4. coin sits in the head. Reeded edge band, then the face, ring, highlight, glyph.
  const dCoin = Math.hypot(sx - g.cx, sy - g.coinCy);
  if (dCoin >= g.faceR && dCoin <= g.reedR) {
    const ang = Math.atan2(sy - g.coinCy, sx - g.cx);
    const tick = Math.floor(((ang + Math.PI) / (2 * Math.PI)) * 44);
    if (tick % 2 === 0) col = REED;
  } else if (dCoin < g.faceR) {
    col = lerp(COIN_HI, COIN_LO, clamp01(dCoin / g.faceR));
    // inner detail ring at ~0.83R.
    if (Math.abs(dCoin - g.faceR * 0.83) < g.faceR * 0.03) col = RING;
    // top-left specular highlight.
    const dh = Math.hypot(sx - (g.cx - g.faceR * 0.32), sy - (g.coinCy - g.faceR * 0.32));
    if (dh < g.faceR * 0.5) col = lerp(col, COIN_HI, (1 - dh / (g.faceR * 0.5)) * 0.45);
    // "$" monogram.
    const cell = g.glyphH / 7;
    const gx = Math.floor((sx - (g.cx - (g.glyphH * 5 / 7) / 2)) / cell);
    const gy = Math.floor((sy - (g.coinCy - g.glyphH / 2)) / cell);
    if (gx >= 0 && gx < 5 && gy >= 0 && gy < 7 && DOLLAR[gy][gx]) col = GLYPH;
  }

  return [col[0], col[1], col[2], 255];
}

function render(size, opts) {
  // Mark geometry, derived once per target. Unit silhouette: head centre at 0.40,
  // head radius 0.27, tip at 0.96 of the mark box (square, centred, nudged up).
  const ms = opts.mark * size;
  const mx = (size - ms) / 2;
  const my = (size - ms) / 2 - ms * 0.02;
  const cx = mx + ms * 0.5;
  const headCy = my + ms * 0.40;
  const r = ms * 0.27;
  const tipX = cx, tipY = my + ms * 0.96;
  const d = tipY - headCy;
  const L = Math.sqrt(Math.max(d * d - r * r, 0));
  const g = {
    cx, headCy, r, tipX, tipY,
    plX: cx - (r * L) / d, plY: headCy + (r * r) / d,
    prX: cx + (r * L) / d, prY: headCy + (r * r) / d,
    coinCy: headCy,
    faceR: ms * 0.20,
    reedR: ms * 0.218,
    glyphH: ms * 0.225,
  };

  const SS = 4; // supersampling factor per axis
  const px = new Uint8Array(size * size * 4);
  for (let y = 0; y < size; y++) {
    for (let x = 0; x < size; x++) {
      let rs = 0, gs = 0, bs = 0, as = 0;
      for (let j = 0; j < SS; j++) {
        for (let i = 0; i < SS; i++) {
          const c = sampleColor(x + (i + 0.5) / SS, y + (j + 0.5) / SS, size, opts, g);
          // premultiply by coverage so transparent edges blend cleanly.
          rs += c[0] * c[3]; gs += c[1] * c[3]; bs += c[2] * c[3]; as += c[3];
        }
      }
      const i = (y * size + x) * 4;
      if (as === 0) { px[i] = px[i + 1] = px[i + 2] = px[i + 3] = 0; continue; }
      px[i] = Math.round(rs / as);
      px[i + 1] = Math.round(gs / as);
      px[i + 2] = Math.round(bs / as);
      px[i + 3] = Math.round(as / (SS * SS));
    }
  }
  return px;
}

const targets = [
  { file: 'icon-192.png', size: 192, padding: 0.06, bleed: false, mark: 0.74 },
  { file: 'icon-512.png', size: 512, padding: 0.06, bleed: false, mark: 0.74 },
  { file: 'icon-maskable-512.png', size: 512, bleed: true, mark: 0.56 }, // logo inside the mask safe zone
  { file: 'apple-touch-icon-180.png', size: 180, bleed: true, mark: 0.72 }, // iOS rounds corners itself
  { file: 'favicon-32.png', size: 32, padding: 0.0, bleed: false, mark: 0.84 },
];
for (const t of targets) {
  writeFileSync(join(OUT, t.file), encodePng(t.size, render(t.size, t)));
  console.log('wrote', t.file);
}
