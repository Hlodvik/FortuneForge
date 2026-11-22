// @vitest-environment jsdom
import { act } from 'react'
import { createRoot, type Root } from 'react-dom/client'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { getSlotSymbolArtwork, getSlotSymbolArtworks, type SlotSymbolArtwork, type SlotSymbolDefinition, type SlotSymbolSet } from '../config/symbolSets'
import { SlotSymbol } from './SlotSymbol'
import { SymbolValueGuide } from './SymbolValueGuide'

const solar: SlotSymbolArtwork = {
  image: '/solar.webp', label: 'Solar Eclipse', animatedImage: '/solar-motion.webp',
  responsiveImages: { 96: '/solar-96.webp', 192: '/solar-192.webp', 256: '/solar-256.webp' },
}
const lunar: SlotSymbolArtwork = { image: '/lunar.webp', label: 'Lunar Eclipse' }
const eclipse: SlotSymbolDefinition = {
  id: 'ECLIPSE', label: 'Eclipse Scatter', image: '/unused-primary.webp',
  animatedImage: '/unused-primary-motion.webp',
  responsiveImages: { 96: '/unused-96.webp', 192: '/unused-192.webp', 256: '/unused-256.webp' },
  artworkVariants: [solar, lunar],
}
const symbolSet: SlotSymbolSet = {
  id: 'artwork-variant-test', definitions: { ECLIPSE: eclipse },
  guideEntries: [{ symbol: 'ECLIPSE', firstLabel: 'Scatter', firstValue: 'Current scatter award' }],
}

let host: HTMLDivElement
let root: Root
beforeEach(() => {
  vi.stubGlobal('IS_REACT_ACT_ENVIRONMENT', true)
  vi.spyOn(HTMLElement.prototype, 'clientWidth', 'get').mockReturnValue(80)
  vi.spyOn(HTMLElement.prototype, 'clientHeight', 'get').mockReturnValue(80)
  host = document.createElement('div')
  document.body.append(host)
  root = createRoot(host)
})
afterEach(() => {
  act(() => root.unmount())
  host.remove()
  vi.restoreAllMocks()
  vi.unstubAllGlobals()
})

describe('optional symbol artwork variants', () => {
  it('uses the full configured set and a stable position, defaulting safely when coordinates are absent or invalid', () => {
    expect(getSlotSymbolArtworks(eclipse)).toBe(eclipse.artworkVariants)
    expect(getSlotSymbolArtwork(eclipse, 0, 0)).toBe(solar)
    expect(getSlotSymbolArtwork(eclipse, 1, 0)).toBe(lunar)
    expect(getSlotSymbolArtwork(eclipse, 0, 1)).toBe(lunar)
    expect(getSlotSymbolArtwork(eclipse, 1, 1)).toBe(solar)
    expect(getSlotSymbolArtwork(eclipse, Number.MAX_SAFE_INTEGER, Number.MAX_SAFE_INTEGER)).toBe(solar)
    for (const [reel, row] of [[undefined, undefined], [0, undefined], [undefined, 0], [-1, 0], [0, -1], [0.5, 0], [NaN, 0], [0, Infinity], [0, Number.MAX_SAFE_INTEGER + 1]])
      expect(getSlotSymbolArtwork(eclipse, reel, row)).toBe(solar)
    expect(getSlotSymbolArtworks({ ...eclipse, artworkVariants: [lunar] })).toEqual([lunar])
    const empty = { ...eclipse, artworkVariants: [] }
    expect(getSlotSymbolArtworks(empty)).toEqual([empty])
  })

  it('renders both forms with the same logical symbol and keeps artwork stable through normal updates', async () => {
    const renderCells = (highlighted: boolean) => <>
      <SlotSymbol symbol="ECLIPSE" symbolSet={symbolSet} reelIndex={0} rowIndex={0} highlighted={highlighted} valueLabel={highlighted ? '7' : '6'} />
      <SlotSymbol symbol="ECLIPSE" symbolSet={symbolSet} reelIndex={1} rowIndex={0} />
    </>
    await act(async () => root.render(renderCells(false)))
    const cells = host.querySelectorAll<HTMLElement>('.slot-symbol')
    expect([...cells].map(cell => cell.dataset.symbol)).toEqual(['ECLIPSE', 'ECLIPSE'])
    expect([...cells].map(cell => cell.getAttribute('aria-label'))).toEqual(['Solar Eclipse', 'Lunar Eclipse'])
    const sources = [...host.querySelectorAll('img')].map(image => image.getAttribute('src'))
    expect(sources).toEqual(['/solar-motion.webp', '/lunar.webp'])
    await act(async () => root.render(renderCells(true)))
    expect([...host.querySelectorAll('img')].map(image => image.getAttribute('src'))).toEqual(sources)
    expect(cells[0].classList.contains('slot-symbol--winner')).toBe(true)
    expect(cells[0].getAttribute('aria-label')).toBe('Solar Eclipse, winning symbol')
    expect(cells[0].querySelector('.slot-symbol__artwork')?.getAttribute('data-value-label')).toBe('7')
  })

  it('keeps responsive and animated sources confined to their selected form', async () => {
    await act(async () => root.render(<>
      <SlotSymbol symbol="ECLIPSE" symbolSet={symbolSet} reelIndex={0} rowIndex={0} animated={false} />
      <SlotSymbol symbol="ECLIPSE" symbolSet={symbolSet} reelIndex={1} rowIndex={0} animated />
    </>))
    const [solarImage, lunarImage] = [...host.querySelectorAll('img')]
    expect(solarImage.getAttribute('src')).toBe('/solar.webp')
    expect(solarImage.getAttribute('srcset')).toContain('/solar-96.webp 96w')
    expect(lunarImage.getAttribute('src')).toBe('/lunar.webp')
    expect(lunarImage.hasAttribute('srcset')).toBe(false)
    expect(lunarImage.hasAttribute('sizes')).toBe(false)
    expect(lunarImage.parentElement?.classList.contains('slot-symbol__artwork--responsive')).toBe(false)
    expect(lunarImage.outerHTML).not.toContain('solar')
    expect(host.innerHTML).not.toContain('/unused-')
  })

  it('falls back to the logical label when the selected artwork has no label', async () => {
    const unlabeled: SlotSymbolSet = { ...symbolSet, definitions: { ECLIPSE: { ...eclipse, artworkVariants: [solar, { image: '/lunar.webp' }] } } }
    await act(async () => root.render(<SlotSymbol symbol="ECLIPSE" symbolSet={unlabeled} reelIndex={1} rowIndex={0} />))
    expect(host.querySelector('.slot-symbol')?.getAttribute('aria-label')).toBe('Eclipse Scatter')
  })

  it('retains singleton static, animated and empty-variant behavior', async () => {
    const legacy = { ...eclipse, id: 'OLD', label: 'Old symbol', image: '/old.webp', animatedImage: '/old-motion.webp', artworkVariants: undefined }
    const originalSet: SlotSymbolSet = { id: 'old-set', definitions: { OLD: legacy, EMPTY: { ...legacy, id: 'EMPTY', artworkVariants: [] } }, guideEntries: [] }
    await act(async () => root.render(<>
      <SlotSymbol symbol="OLD" symbolSet={originalSet} reelIndex={1} rowIndex={3} animated={false} />
      <SlotSymbol symbol="OLD" symbolSet={originalSet} animated />
      <SlotSymbol symbol="EMPTY" symbolSet={originalSet} reelIndex={1} rowIndex={0} animated={false} />
    </>))
    const images = [...host.querySelectorAll('img')]
    expect(images.map(image => image.getAttribute('src'))).toEqual(['/old.webp', '/old-motion.webp', '/old.webp'])
    expect(images[0].getAttribute('srcset')).toContain('/unused-96.webp 96w')
    expect(images[1].hasAttribute('srcset')).toBe(false)
    expect(host.querySelector('.slot-symbol')?.getAttribute('aria-label')).toBe('Old symbol')
  })

  it('shows both static forms in one logical guide entry and one award row', async () => {
    await act(async () => root.render(<SymbolValueGuide symbolSet={symbolSet} isOpen showValueTokens={false} />))
    const rows = document.querySelectorAll('.symbol-value-guide__item')
    expect(rows).toHaveLength(1)
    expect(rows[0].classList.contains('symbol-value-guide__item--artwork-variants')).toBe(true)
    const images = [...rows[0].querySelectorAll('img')]
    expect(images.map(image => image.getAttribute('src'))).toEqual(['/solar.webp', '/lunar.webp'])
    expect(images.map(image => image.alt)).toEqual(['Solar Eclipse', 'Lunar Eclipse'])
    expect(images[0].getAttribute('srcset')).toContain('/solar-96.webp 96w')
    expect(images[1].hasAttribute('srcset')).toBe(false)
    expect(rows[0].querySelectorAll('.symbol-value-guide__name')).toHaveLength(1)
    expect(rows[0].textContent?.match(/Current scatter award/g)).toHaveLength(1)
    expect(rows[0].innerHTML).not.toContain('motion.webp')
  })

  it('keeps a guide without variants on the existing singleton layout', async () => {
    const single: SlotSymbolSet = { ...symbolSet, definitions: { ECLIPSE: { ...eclipse, artworkVariants: undefined } } }
    await act(async () => root.render(<SymbolValueGuide symbolSet={single} isOpen showValueTokens={false} />))
    const row = document.querySelector('.symbol-value-guide__item')!
    expect(row.classList.contains('symbol-value-guide__item--artwork-variants')).toBe(false)
    expect(row.hasAttribute('style')).toBe(false)
    expect(row.querySelectorAll('img')).toHaveLength(1)
    expect(row.querySelector('img')?.getAttribute('alt')).toBe('')
    expect(row.querySelector('img')?.getAttribute('aria-hidden')).toBe('true')
  })
})
