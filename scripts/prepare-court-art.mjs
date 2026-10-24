// Historical portrait experiment, not used by the current shared PlayingCard.
// Approved lightweight icons live in CourtCardSymbol.tsx instead.
// Extract only the illustrated court figures from the existing vector deck.
// Do not import its card backgrounds, inner frames, ranks, or corner suits.
// Usage: node scripts/prepare-court-art.mjs <source-svg-directory> <sharp-module-path>
import { mkdir, readFile, writeFile } from 'node:fs/promises'
import path from 'node:path'
import { fileURLToPath } from 'node:url'
import { createRequire } from 'node:module'
import { JSDOM } from 'jsdom'

const require = createRequire(import.meta.url)
const sharp = require(process.argv[3] || 'sharp')
const source = process.argv[2]
if (!source) throw new Error('Pass the directory containing the original *_of_*2.svg court illustrations.')

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..')
const destination = path.join(root, 'fortuneforge.client/src/assets/cards/courts')
const reviewDirectory = path.join(root, '.tmp/court-art-review')
await mkdir(destination, { recursive: true })
await mkdir(reviewDirectory, { recursive: true })

for (const rank of ['jack', 'queen', 'king']) {
  for (const suit of ['clubs', 'diamonds', 'hearts', 'spades']) {
    const svg = await readFile(path.join(source, `${rank}_of_${suit}2.svg`), 'utf8')
    const document = new JSDOM(svg, { contentType: 'image/svg+xml' }).window.document
    const figures = [...document.documentElement.children].filter(element =>
      element.localName === 'g' && /^g\d+$/.test(element.id),
    )
    if (figures.length !== 1) throw new Error(`Expected one court illustration for ${rank} of ${suit}.`)

    // Keep the original dark linework and facial details, but remove the
    // decorative coloured fills. Each figure uses just its suit's ink colour.
    const ink = suit === 'hearts' || suit === 'diamonds' ? '#b02f36' : '#17201e'
    let inkPaths = 0
    for (const element of figures[0].querySelectorAll('path')) {
      const fill = element.getAttribute('style')?.match(/fill:(#[a-f\d]+)/i)?.[1]
      if (fill === '#1156a1' || fill === '#000000' || fill === '#000400') {
        element.setAttribute('style', `fill:${ink}`)
        inkPaths++
      } else {
        element.remove()
      }
    }
    if (inkPaths < 2) throw new Error(`Missing court linework for ${rank} of ${suit}.`)

    // All twelve source illustrations use the same art bounds. The surrounding
    // rounded rectangle is a separate group, deliberately excluded here.
    const portrait = `<svg xmlns="http://www.w3.org/2000/svg" viewBox="22 22 124 199" width="200" height="321">${figures[0].outerHTML}</svg>`
    const name = `${rank}-${suit}`
    await writeFile(path.join(reviewDirectory, `${name}.svg`), portrait)
    const image = await sharp(Buffer.from(portrait)).resize({ width: 200 }).webp({ quality: 85, alphaQuality: 100 }).toBuffer()
    await writeFile(path.join(destination, `${name}.webp`), image)
    console.log(`${name}: ${image.length} bytes`)
  }
}
