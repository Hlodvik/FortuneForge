import assert from 'node:assert/strict';
import { createRequire } from 'node:module';
import { readFile, writeFile, mkdir } from 'node:fs/promises';
import { fileURLToPath } from 'node:url';
import { resolve, dirname } from 'node:path';

const ids = new Set(['wukong-journey-to-the-west', 'rainbow-realm', 'pirates-fortune',
  'gods-of-olympus', 'reel-riches', 'high-noon-fortune', 'royal-draw', 'arcane-archives',
  'cosmic-fortune', 'dino-dominion', 'neon-nights', 'jungle-jackpot', 'ocean-odyssey',
  'samurai-fortune', 'candy-carnival', 'phantom-manor', 'nordic-legends', 'desert-treasures',
  'robot-revolution', 'dragon-hoard']);
const [id, source, version = '1'] = process.argv.slice(2);
assert.ok(ids.has(id), 'Choose one of the 20 assigned slot game IDs.');
assert.ok(source, 'Supply the generated source image path.');
assert.match(version, /^[1-9]\d*$/, 'Version must be a positive integer.');
const root = fileURLToPath(new URL('../', import.meta.url));
const out = resolve(root, `fortuneforge.client/src/assets/catalogue/slots/${id}-title-v${version}.webp`);
const artworkRequire = createRequire(new URL('../tools/artwork/package.json', import.meta.url));
const sharp = artworkRequire('sharp');
const input = await readFile(resolve(source));
const image = sharp(input).rotate().resize(768, 384, { fit: 'cover', position: 'centre' });
let quality = 84;
let data = await image.clone().webp({ quality, effort: 6 }).toBuffer();
while (data.length > 180 * 1024 && quality > 52) {
  quality -= 4;
  data = await image.clone().webp({ quality, effort: 6 }).toBuffer();
}
assert.ok(data.length <= 180 * 1024, 'Asset is too large; simplify the artwork or choose another export.');
await mkdir(dirname(out), { recursive: true });
await writeFile(out, data, { flag: 'wx' });
console.log(JSON.stringify({ file: out, width: 768, height: 384, bytes: data.length, quality }));
