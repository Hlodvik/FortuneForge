import { createRequire } from 'node:module'
import { existsSync } from 'node:fs'
import { copyFile, mkdir, writeFile } from 'node:fs/promises'
import { fileURLToPath } from 'node:url'
import { resolve } from 'node:path'

// Slice the original generated atlas and optimize delivery assets without changing its artwork.
const root = fileURLToPath(new URL('../../../', import.meta.url))
const require = createRequire(new URL('../../../tools/artwork/package.json', import.meta.url))
const sharp = require('sharp')
const [temple, atlas, title, wordmark, frame, rooms, cutaway] = process.argv.slice(2)
if (!temple || !atlas || !title) throw new Error('Supply temple, 4×4 symbol atlas, and title PNG paths.')
const output = resolve(root, 'games/CosmicFortune/client/FortuneForge.Games.CosmicFortune.Client/src/assets/art/pharaohs-ascent')
const source = resolve(root, 'games/CosmicFortune/artwork/source')
await Promise.all([mkdir(output, { recursive: true }), mkdir(source, { recursive: true })])
const inputs = { temple, atlas, title, ...(wordmark && { wordmark }), ...(frame && { frame }), ...(rooms && { rooms }), ...(cutaway && { cutaway }) }
for (const [name, path] of Object.entries(inputs)) await copyFile(path, resolve(source, `${name}-v1.png`))
await sharp(temple).resize(1920, 1080, { fit: 'cover' }).webp({ quality: 85 }).toFile(resolve(output, 'temple-v1.webp'))
await sharp(title).resize(960, 480, { fit: 'cover' }).webp({ quality: 88 }).toFile(resolve(output, 'title-v1.webp'))
if (wordmark) await sharp(wordmark).trim().resize(1280, 280, { fit: 'contain', background: '#00000000' })
  .webp({ quality: 92, alphaQuality: 100 }).toFile(resolve(output, 'wordmark-v1.webp'))
if (frame) await sharp(frame).resize(1536, 1024, { fit: 'fill' })
  .webp({ quality: 92, alphaQuality: 100 }).toFile(resolve(output, 'cabinet-frame-v1.webp'))
if (cutaway) await sharp(cutaway).resize(1024, 1024, { fit: 'contain', background: '#00000000' })
  .webp({ quality: 90, alphaQuality: 100 }).toFile(resolve(output, 'tomb-cutaway-v1.webp'))
if (rooms) {
  const { width: roomWidth, height: roomHeight } = await sharp(rooms).metadata()
  const roomNames = ['coin-room', 'relic-room', 'key-room', 'sarcophagus']
  for (let index = 0; index < roomNames.length; index++) {
    const left = Math.round(roomWidth * (index % 2) / 2), top = Math.round(roomHeight * Math.floor(index / 2) / 2)
    const width = Math.round(roomWidth * (index % 2 + 1) / 2) - left
    const height = Math.round(roomHeight * (Math.floor(index / 2) + 1) / 2) - top
    await sharp(rooms).extract({ left, top, width, height }).resize(384, 384, { fit: 'contain', background: '#00000000' })
      .webp({ quality: 90, alphaQuality: 100 }).toFile(resolve(output, `${roomNames[index]}-v1.webp`))
  }
}
const catalogue = resolve(root, 'fortuneforge.client/src/assets/catalogue/slots/pharaohs-ascent-title-v1.webp')
await sharp(title).resize(768, 384, { fit: 'cover' }).webp({ quality: 85 }).toFile(catalogue)
const names = ['ankh', 'eye', 'winged-scarab', 'lotus', 'falcon', 'pharaoh', 'gate', 'sun',
  'sunfire', 'trail-scarab', 'chest', 'coin', 'horus', 'thoth', 'anubis', 'wadjet']
const { width, height } = await sharp(atlas).metadata()
for (let index = 0; index < names.length; index++) {
  const x = index % 4, y = Math.floor(index / 4)
  const left = Math.round(width * x / 4), top = Math.round(height * y / 4)
  const tile = { left, top, width: Math.round(width * (x + 1) / 4) - left, height: Math.round(height * (y + 1) / 4) - top }
  await sharp(atlas).extract(tile).resize(256, 256, { fit: 'contain', background: '#00000000' })
    .webp({ quality: 90, alphaQuality: 100 }).toFile(resolve(output, `${names[index]}-v1.webp`))
}
// Individually generated refinements supersede their atlas tile without losing the original source.
for (const [original, delivery] of [['sun-clean-v1.png', 'sun-v1.webp'], ['wild-v1.png', 'wild-v1.webp']]) {
  const refined = resolve(source, original)
  if (existsSync(refined)) await sharp(refined).resize(256, 256, { fit: 'contain', background: '#00000000' })
    .webp({ quality: 92, alphaQuality: 100 }).toFile(resolve(output, delivery))
}
await writeFile(resolve(source, 'README.txt'), 'Original artwork generated with the built-in imagegen tool for Pharaoh’s Ascent.\nPrompts and generation details: ../prompts.json\nRun ../prepare-pharaoh-art.mjs with temple, atlas and title paths to reproduce delivery assets.\n')
console.log(JSON.stringify({ output, catalogue, symbols: names.length }))
