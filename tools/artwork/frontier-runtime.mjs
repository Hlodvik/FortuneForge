import { mkdir, readdir, stat, writeFile } from 'node:fs/promises'
import { dirname, join, relative, sep } from 'node:path'
import { fileURLToPath } from 'node:url'
import sharp from 'sharp'

const game = fileURLToPath(new URL('../../games/HighNoonFortune/', import.meta.url))
const assets = join(game, 'client/FortuneForge.Games.FrontierFortune.Client/src/assets/frontier')
const originals = join(game, 'artwork/source')
const selected = new Set(process.argv.slice(2).map(name => name.replaceAll('\\', '/')))
const runtimeWidths = { 'title.png': 1200, 'cabinet/cabinet-surround.png': 1600 }
async function files(directory) {
  return (await Promise.all((await readdir(directory, { withFileTypes: true })).map(entry =>
    entry.isDirectory() ? files(join(directory, entry.name)) : [join(directory, entry.name)]))).flat()
}
async function cabinetGeometry(image) {
  const { data, info } = await sharp(image).ensureAlpha().raw().toBuffer({ resolveWithObject: true })
  const apertureAlphaLimit = 1
  const centerX = Math.floor(info.width / 2)
  const centerY = Math.floor(info.height / 2)
  if (data[(centerY * info.width + centerX) * info.channels + 3] !== 0) {
    throw new Error('The Frontier cabinet must have a genuinely transparent center.')
  }
  const heights = new Int32Array(info.width)
  let best = { left: 0, top: 0, width: 0, height: 0 }
  for (let y = 0; y < info.height; y++) {
    for (let x = 0; x < info.width; x++) {
      heights[x] = data[(y * info.width + x) * info.channels + 3] <= apertureAlphaLimit ? heights[x] + 1 : 0
    }
    const stack = []
    for (let x = 0; x <= info.width; x++) {
      const height = x === info.width ? 0 : heights[x]
      while (stack.length && heights[stack.at(-1)] > height) {
        const rectangleHeight = heights[stack.pop()]
        const left = stack.length ? stack.at(-1) + 1 : 0
        const top = y - rectangleHeight + 1
        const width = x - left
        if (left <= centerX && x > centerX && top <= centerY && y >= centerY
          && width * rectangleHeight > best.width * best.height) {
          best = { left, top, width, height: rectangleHeight }
        }
      }
      stack.push(x)
    }
  }
  if (best.width * best.height < info.width * info.height * .55) {
    throw new Error('The Frontier cabinet aperture is too small for the actual reels.')
  }
  const opening = { left: best.left + 2, top: best.top + 2, width: best.width - 4, height: best.height - 4 }
  return {
    imageWidth: info.width, imageHeight: info.height, centerAlpha: 0, maximumApertureAlpha: apertureAlphaLimit,
    safeOpening: opening,
    insetPercent: {
      top: opening.top / info.height * 100,
      right: (info.width - opening.left - opening.width) / info.width * 100,
      bottom: (info.height - opening.top - opening.height) / info.height * 100,
      left: opening.left / info.width * 100,
    },
  }
}
let sourceBytes = 0
let runtimeBytes = 0
const active = (await files(originals)).filter(file => file.endsWith('.png')).map(source => ({
  source, name: relative(originals, source).split(sep).join('/'),
})).filter(({ name }) => !name.startsWith('bonus/'))
for (const name of selected) {
  if (!active.some(asset => asset.name === name)) throw new Error(`Unknown or retired Frontier artwork: ${name}`)
}
for (const { source, name } of active.filter(asset => selected.size === 0 || selected.has(asset.name))) {
  const width = name.startsWith('backgrounds/') ? 1920 : runtimeWidths[name] ?? 512
  const destination = join(assets, name.replace(/\.png$/, '.webp'))
  await mkdir(dirname(destination), { recursive: true })
  await sharp(source).resize({ width, height: width, fit: 'inside', withoutEnlargement: true })
    .webp({ quality: 88, alphaQuality: 100, effort: 6 }).toFile(destination)
  if (name === 'cabinet/cabinet-surround.png') {
    const geometry = await cabinetGeometry(destination)
    await writeFile(destination.replace(/\.webp$/, '.geometry.json'), JSON.stringify(geometry, null, 2) + '\n')
  }
  sourceBytes += (await stat(source)).size
  runtimeBytes += (await stat(destination)).size
}
const title = join(assets, 'title.webp')
const catalogue = fileURLToPath(new URL('../../fortuneforge.client/src/assets/catalogue/slots/frontier-fortune-title.webp', import.meta.url))
if (selected.size === 0 || selected.has('title.png')) {
  await sharp(title).resize(768, 384, { fit: 'contain', background: '#00000000' }).webp({ quality: 88, alphaQuality: 100 }).toFile(catalogue)
}
console.log(JSON.stringify({ sourceBytes, runtimeBytes, savedPercent: Math.round((1 - runtimeBytes / sourceBytes) * 100), originals, assets }))
