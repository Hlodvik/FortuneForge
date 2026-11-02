import { readFileSync } from 'node:fs'
import { dirname, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'
import { expect, test, type Page } from '@playwright/test'
import { tileClass, tileTone } from '../../games/DropMerge/client/FortuneForge.Games.DropMerge.Client/src/dropMergeHelpers'

const repository = resolve(dirname(fileURLToPath(import.meta.url)), '../..')

function gameStyles(game: string) {
  const source = resolve(repository, `games/${game}/client/FortuneForge.Games.${game}.Client/src`)
  const entry = readFileSync(resolve(source, 'index.ts'), 'utf8')
  return [...entry.matchAll(/import\s+['"]\.\/([^'"]+\.css)['"]/g)]
    .map(match => readFileSync(resolve(source, match[1]), 'utf8')).join('\n')
}

const dropMergeStyles = gameStyles('DropMerge')
const puzzleStyles = gameStyles('TwentyFortyEight')
const values = [2, 4, 8, 16, 32, 64, 128, 256, 512, 1024, 2048]
const fixture = `<style>body { margin: 13px; background: rgb(19, 23, 29); color: rgb(220, 221, 222) }</style>
  <main class="ff-2048-page">${values.map(value => `<div class="ff-2048-tile tile-${value}">${value}</div>`).join('')}<div class="ff-2048-tile tile-super">4096</div></main>
  <section class="ff-drop-merge-page">${Array.from({ length: 40 }, (_, rank) => {
    const value = 2 ** (rank + 1)
    return `<span class="ff-drop-merge-tile ${tileClass(value)} ${tileTone(value)}">${value}</span>`
  }).join('')}</section>
  <aside id="outside-game" class="soft warm gold bright tile-rank-0">Unrelated content</aside>`

async function puzzleAppearance(page: Page) {
  return page.evaluate(() => {
    const appearance = (element: Element) => {
      const style = getComputedStyle(element)
      return [style.backgroundColor, style.backgroundImage, style.color, style.fontFamily,
        style.fontSize, style.margin, style.boxSizing, style.boxShadow,
        style.getPropertyValue('--tile-color')]
    }
    return {
      root: appearance(document.documentElement),
      body: appearance(document.body),
      tiles: Array.from(document.querySelectorAll('.ff-2048-tile'), appearance),
      outside: appearance(document.querySelector('#outside-game')!),
    }
  })
}

async function dropMergePalette(page: Page) {
  return page.locator('.ff-drop-merge-tile').evaluateAll(tiles => tiles.map(tile => {
    const style = getComputedStyle(tile)
    return { token: style.getPropertyValue('--tile-color').trim(), color: style.backgroundColor }
  }))
}

test('loading Drop Merge after 2048 preserves puzzle tiles and host styles', async ({ page }) => {
  await page.setContent(fixture)
  await page.addStyleTag({ content: puzzleStyles })
  const before = await puzzleAppearance(page)
  await page.addStyleTag({ content: dropMergeStyles })
  expect(await puzzleAppearance(page)).toEqual(before)

  const palette = await dropMergePalette(page)
  expect(palette).toHaveLength(40)
  expect(palette.every(tile => tile.token !== '' && tile.color !== 'rgba(0, 0, 0, 0)')).toBe(true)
  expect(new Set(palette.map(tile => tile.color)).size).toBe(40)
})

test('Drop Merge rank colors survive loading 2048 afterward', async ({ page }) => {
  await page.setContent(fixture)
  await page.addStyleTag({ content: dropMergeStyles })
  const before = await dropMergePalette(page)
  await page.addStyleTag({ content: puzzleStyles })
  expect(await dropMergePalette(page)).toEqual(before)
})

test('Drop Merge alone leaves document resets and unrelated elements untouched', async ({ page }) => {
  await page.setContent(fixture)
  const before = await puzzleAppearance(page)
  await page.addStyleTag({ content: dropMergeStyles })
  expect(await puzzleAppearance(page)).toEqual(before)
})
