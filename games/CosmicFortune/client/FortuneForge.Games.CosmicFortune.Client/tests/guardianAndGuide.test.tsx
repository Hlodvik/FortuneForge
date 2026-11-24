// @vitest-environment jsdom
import { act, type ComponentProps } from 'react'
import { createHash } from 'node:crypto'
import { readFileSync } from 'node:fs'
import { URL as NodeURL } from 'node:url'
import { createRoot, type Root } from 'react-dom/client'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { PharaohAscentPanel } from '../src/PharaohAscentPanel'
import { decodePharaohFeature, decodePharaohState, type PharaohFeature, type PharaohState } from '../src/pharaoh'
import { COSMIC_FORTUNE_SYMBOLS } from '../src/symbols'
import { PharaohHelp } from '../src/PharaohHelp'
import { PHARAOH_MATH } from '../src/math'

const savedState = (): PharaohState => ({
  kind: 'pharaoh-ascent',
  collections: [
    { sealId: 'sync', count: 12, requiredCount: 24, averageWagerPoints: 300 },
    { sealId: 'rows', count: 23, requiredCount: 24, averageWagerPoints: 250 },
    { sealId: 'paw', count: 4, requiredCount: 24, averageWagerPoints: 200 },
    { sealId: 'rand', count: 0, requiredCount: 24, averageWagerPoints: 0 },
  ],
  freeSpinsRemaining: 7,
  freeSpinFeatureMode: 'sync-rows',
  freeSpinWagerPoints: 300,
  energyBalance: 65,
})

const paidTrail = (): PharaohFeature => ({
  kind: 'pharaoh-ascent',
  scarabTrails: [{
    positions: [{ reel: 0, row: 1 }, { reel: 1, row: 1 }, { reel: 1, row: 2 }],
    symbolCount: 3, multiplier: 3, amountPoints: 500,
  }],
  scarabTrailPoints: 500,
})

describe('Pharaoh’s Ascent server receipt decoding', () => {
  it('accepts four saved guardian tracks and combined guardian mode without recalculating money', () => {
    const state = savedState()
    expect(decodePharaohState(state)).toEqual(state)
    const receipt = paidTrail()
    expect(decodePharaohFeature(receipt)).toEqual(receipt)
    expect(decodePharaohFeature({ kind: 'pharaoh-ascent', scarabTrails: [], scarabTrailPoints: 0 }))
      .toEqual({ kind: 'pharaoh-ascent', scarabTrails: [], scarabTrailPoints: 0 })
  })

  it('rejects missing, repeated and unrecognized guardian tracks', () => {
    const missing = savedState()
    missing.collections.pop()
    expect(decodePharaohState(missing)).toBeNull()
    const repeated = savedState()
    repeated.collections[3] = { ...repeated.collections[0] }
    expect(decodePharaohState(repeated)).toBeNull()
    const unknown = { ...savedState(), collections: [...savedState().collections.slice(0, 3), {
      sealId: 'unknown', count: 0, requiredCount: 24, averageWagerPoints: 0,
    }] }
    expect(decodePharaohState(unknown)).toBeNull()
    const wrongTarget = savedState()
    wrongTarget.collections[0].requiredCount = 40
    expect(decodePharaohState(wrongTarget)).toBeNull()
  })

  it('rejects unsafe or fractional persisted quantities and unavailable payloads', () => {
    for (const value of [null, undefined, [], {}, { ...savedState(), kind: 'other-game' }]) {
      expect(decodePharaohState(value)).toBeNull()
      expect(decodePharaohFeature(value)).toBeNull()
    }
    for (const invalid of [-1, 0.5, Number.NaN, Number.POSITIVE_INFINITY, Number.MAX_SAFE_INTEGER + 1]) {
      expect(decodePharaohState({ ...savedState(), energyBalance: invalid })).toBeNull()
      expect(decodePharaohState({ ...savedState(), freeSpinWagerPoints: invalid })).toBeNull()
      expect(decodePharaohFeature({ ...paidTrail(), scarabTrailPoints: invalid })).toBeNull()
    }
  })

  it('refuses ambiguous scarab receipts with mismatched totals, reused positions or incorrect tiers', () => {
    expect(decodePharaohFeature({ ...paidTrail(), scarabTrailPoints: 501 })).toBeNull()
    const reused = paidTrail()
    reused.scarabTrails[0].positions[2] = { ...reused.scarabTrails[0].positions[0] }
    expect(decodePharaohFeature(reused)).toBeNull()
    const repeatedGroup = paidTrail()
    repeatedGroup.scarabTrails.push({ ...repeatedGroup.scarabTrails[0] })
    repeatedGroup.scarabTrailPoints = 1000
    expect(decodePharaohFeature(repeatedGroup)).toBeNull()
    const wrongTier = paidTrail()
    wrongTier.scarabTrails[0].multiplier = 5
    expect(decodePharaohFeature(wrongTier)).toBeNull()
    const wrongCount = paidTrail()
    wrongCount.scarabTrails[0].symbolCount = 4
    expect(decodePharaohFeature(wrongCount)).toBeNull()
  })

  it('accepts edge-connected trails in the sixth row and rejects diagonal or disconnected groups', () => {
    const expanded = paidTrail()
    expanded.scarabTrails[0].positions = [{ reel: 2, row: 5 }, { reel: 3, row: 5 }, { reel: 4, row: 5 }]
    expect(decodePharaohFeature(expanded)).toEqual(expanded)
    const disconnected = paidTrail()
    disconnected.scarabTrails[0].positions = [{ reel: 0, row: 0 }, { reel: 1, row: 1 }, { reel: 2, row: 2 }]
    expect(decodePharaohFeature(disconnected)).toBeNull()
    const outsideReels = paidTrail()
    outsideReels.scarabTrails[0].positions[2] = { reel: 5, row: 2 }
    expect(decodePharaohFeature(outsideReels)).toBeNull()
    const outsideRows = paidTrail()
    outsideRows.scarabTrails[0].positions[2] = { reel: 1, row: 6 }
    expect(decodePharaohFeature(outsideRows)).toBeNull()
  })

  it('accepts separate groups but rejects adjacent groups split into two separate rewards', () => {
    const receipt = paidTrail()
    receipt.scarabTrails.push({
      positions: [{ reel: 2, row: 5 }, { reel: 3, row: 5 }, { reel: 4, row: 5 }],
      symbolCount: 3, multiplier: 3, amountPoints: 250,
    })
    receipt.scarabTrailPoints = 750
    expect(decodePharaohFeature(receipt)).toEqual(receipt)
    receipt.scarabTrails[1].positions = [{ reel: 2, row: 1 }, { reel: 3, row: 1 }, { reel: 4, row: 1 }]
    expect(decodePharaohFeature(receipt)).toBeNull()
  })
})

describe('Pharaoh’s Ascent visible symbol rules', () => {
  it('keeps the illustrated payline snapshot generated from the authoritative engine layouts', () => {
    const factory = readFileSync(new NodeURL('../../../../shared/FortuneForge.Games.Slots.Runtime/ClassicSlotDefinitionFactory.cs', import.meta.url), 'utf8')
    const snapshot = JSON.parse(readFileSync(new NodeURL('../src/assets/rules/payline-layouts.json', import.meta.url), 'utf8')) as { sourceSha256: string; patterns: number[][] }
    const declaration = /CreatePaylines\(\) =>\s*(\[[\s\S]*?\]);/.exec(factory)
    expect(declaration).not.toBeNull()
    expect(snapshot.patterns).toEqual(JSON.parse(declaration![1]))
    expect(snapshot.patterns).toHaveLength(23)
    expect(snapshot.sourceSha256).toBe(createHash('sha256').update(factory).digest('hex'))
  })

  it('uses the authoritative integer paytable, the actual scatter award and the 24-seal target', () => {
    const guide = COSMIC_FORTUNE_SYMBOLS.guideEntries
    expect(PHARAOH_MATH.payoutMultiplier).toBe(1)
    for (const rule of PHARAOH_MATH.payouts) {
      expect(Number.isSafeInteger(rule.multiplier)).toBe(true)
      const entry = guide.find(entry => entry.symbol === rule.symbolId)!
      expect(rule.matchLength === 3 ? entry.firstValue : entry.secondValue).toBe(`${rule.multiplier}×`)
    }
    expect(guide.find(entry => entry.symbol === '2')?.firstLabel).toBe('3–4')
    expect(guide.find(entry => entry.symbol === 'ACE')?.firstLabel).toBe('3')
    expect(guide.find(entry => entry.symbol === 'FREE')).toMatchObject({ firstLabel: '3+', secondValue: '6 Tomb Spins' })
    expect(guide.filter(entry => entry.symbol.startsWith('SEAL_'))).toHaveLength(4)
    expect(guide.filter(entry => entry.symbol.startsWith('SEAL_')).every(entry => entry.firstValue === 'collect 24')).toBe(true)
    expect(COSMIC_FORTUNE_SYMBOLS.guideNote).toContain('Ordinary four-symbol edge runs pay the three-symbol rate')
    expect(COSMIC_FORTUNE_SYMBOLS.guideNote).toContain('substitutes only on five-symbol paylines')
  })

  it('distinguishes connected red scarab rewards and excludes unreachable collector symbols', () => {
    expect(COSMIC_FORTUNE_SYMBOLS.guideEntries.find(entry => entry.symbol === 'BANANA'))
      .toMatchObject({ firstLabel: '3 / 4 / 5+', firstValue: 'connected', secondValue: '3× / 5× / 8×' })
    expect(Object.keys(COSMIC_FORTUNE_SYMBOLS.definitions).some(id => id === 'PAW' || id.startsWith('RAND_'))).toBe(false)
    expect(COSMIC_FORTUNE_SYMBOLS.guideEntries.some(entry => entry.symbol === 'PAW' || entry.symbol.startsWith('RAND_'))).toBe(false)
  })
})

let host: HTMLDivElement
let root: Root
type PanelProps = ComponentProps<typeof PharaohAscentPanel>
let defaults: PanelProps
let fetcher: ReturnType<typeof vi.fn>

beforeEach(() => {
  vi.stubGlobal('IS_REACT_ACT_ENVIRONMENT', true)
  fetcher = vi.fn()
  vi.stubGlobal('fetch', fetcher)
  host = document.createElement('div')
  document.body.append(host)
  root = createRoot(host)
  defaults = {
    state: null, feature: null, spinKey: null, isSpinning: false, reducedMotion: true,
    pointValueInCents: 25, onOpen: vi.fn(),
  }
})
afterEach(() => {
  act(() => root.unmount())
  host.remove()
  vi.unstubAllGlobals()
})
async function mount(overrides: Partial<PanelProps> = {}) {
  await act(async () => root.render(<PharaohAscentPanel {...defaults} {...overrides} />))
}
const guardianButton = (name: string) => Array.from(host.querySelectorAll<HTMLButtonElement>('button'))
  .find(button => button.getAttribute('aria-label')?.startsWith(name))!

describe('Pharaoh’s Ascent guardian panel', () => {
  it('explains the Sealed Tomb decisions and true reel rules without an obsolete feature name', async () => {
    const close = vi.fn()
    await act(async () => root.render(<PharaohHelp onClose={close} />))
    expect(host.textContent).toContain('The Sealed Tomb')
    expect(host.textContent).toContain('Six sigils unlock an expedition')
    expect(host.textContent).toContain('3 or more red scarabs anywhere')
    expect(host.textContent).not.toContain('0.856')
    expect(host.textContent).toContain('lower of your current bet and the average bet that filled the meter')
    expect(host.textContent).toContain('The bonus and improved seal chances keep the value of the wagers that filled the meter.')
    expect(host.textContent).toContain('New funded averages round down to whole points')
    expect(host.textContent).toContain('Fractional point bonuses round down')
    expect(host.textContent).toContain('including rooms already opened')
    await act(async () => Array.from(host.querySelectorAll<HTMLButtonElement>('button')).find(button => button.textContent === 'Four symbols')!.click())
    expect(host.textContent).toContain('four-symbol runs at either edge pay the three-symbol rate')
    expect(host.textContent).not.toContain('four adjacent matches do not pay')
    expect(host.textContent?.toLowerCase()).not.toContain('treasury')
    await act(async () => host.querySelector<HTMLButtonElement>('.pharaoh-guide__ending button')!.click())
    expect(close).toHaveBeenCalledOnce()
    expect(fetcher).not.toHaveBeenCalled()
  })

  it('lets readers inspect passages, relic sets, scarab connections and all payline shapes without gameplay actions', async () => {
    await act(async () => root.render(<PharaohHelp onClose={vi.fn()} />))
    const buttonWithText = (text: string) => Array.from(host.querySelectorAll<HTMLButtonElement>('button')).find(button => button.textContent === text)!
    const keyTab = host.querySelector<HTMLButtonElement>('[role="tab"][id="pharaoh-guide-passage-key"]')!
    await act(async () => keyTab.click())
    expect(host.querySelector('[role="tabpanel"]')?.textContent).toContain('The key chamber')
    expect(host.querySelector('[role="tabpanel"]')?.textContent).toContain('third key opens the sarcophagus')
    await act(async () => keyTab.dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowLeft', bubbles: true })))
    expect(host.querySelector('[role="tabpanel"]')?.textContent).toContain('The relic chamber')
    expect(document.activeElement?.id).toBe('pharaoh-guide-passage-relic')
    await act(async () => buttonWithText('2 full sets').click())
    expect(host.querySelector('.pharaoh-guide__multiplier strong')?.textContent).toBe('3×')
    expect(host.querySelector('.pharaoh-guide__bank-example strong')?.textContent).toBe('23×')
    expect(host.querySelector('.pharaoh-guide__bank-example strong')?.textContent).toBe('23×')
    await act(async () => buttonWithText('Diagonal corners').click())
    expect(host.querySelector('.pharaoh-guide__example-caption')?.textContent).toContain('do not earn a trail reward')
    await act(async () => host.querySelector<HTMLButtonElement>('[aria-label="Previous payline pattern"]')!.click())
    expect(host.querySelector('.pharaoh-guide__payline-pager strong')?.textContent).toBe('23')
    expect(host.querySelectorAll('[aria-label^="Inspect payline "]')).toHaveLength(23)
    await act(async () => host.querySelector<HTMLButtonElement>('[aria-label="Inspect payline 17"]')!.click())
    expect(host.querySelector('.pharaoh-guide__payline-pager strong')?.textContent).toBe('17')
    await act(async () => buttonWithText('With a wild').click())
    expect(host.querySelector('.pharaoh-guide__payline-stage svg text')?.textContent).toBe('WILD')
    expect(fetcher).not.toHaveBeenCalled()
  })

  it('shows loading state without inventing saved seals or a paid award', async () => {
    await mount()
    expect(host.textContent).toContain('Loading your saved seals')
    expect(guardianButton('Horus').getAttribute('aria-label')).toContain('progress loading')
    expect(host.querySelector('[role="progressbar"]')?.getAttribute('aria-valuenow')).toBeNull()
    expect(host.textContent).not.toContain('This reward is already included')
    expect(fetcher).not.toHaveBeenCalled()
  })

  it('shows saved progress and combined guardian activity; inspecting a chamber never changes gameplay', async () => {
    await mount({ state: savedState() })
    expect(guardianButton('Horus').getAttribute('aria-label')).toBe('Horus: 12 of 24 seals, guardian active')
    expect(guardianButton('Thoth').getAttribute('aria-label')).toBe('Thoth: 23 of 24 seals, guardian active')
    expect(guardianButton('Anubis').getAttribute('aria-label')).toBe('Anubis: 4 of 24 seals')
    expect(host.querySelector('[role="progressbar"]')?.getAttribute('aria-valuenow')).toBe('65')
    await act(async () => guardianButton('Wadjet').click())
    expect(guardianButton('Wadjet').getAttribute('aria-pressed')).toBe('true')
    expect(guardianButton('Horus').getAttribute('aria-pressed')).toBe('false')
    expect(host.textContent).toContain('An entire reel becomes wild')
    expect(host.textContent).toContain('24 seals to 7 enhanced Tomb Spins')
    expect(defaults.onOpen).toHaveBeenCalledTimes(1)
    expect(fetcher).not.toHaveBeenCalled()
  })

  it('presents the settled ZAR amount once and removes its receipt while another spin runs', async () => {
    const receipt = paidTrail()
    await mount({ state: savedState(), feature: receipt, spinKey: 1 })
    expect(host.textContent?.replaceAll('\u00a0', ' ')).toMatch(/R\s?125[.,]00/)
    expect(host.textContent).toContain('included')
    await mount({ state: savedState(), feature: receipt, spinKey: 1, isSpinning: true })
    expect(host.textContent?.replaceAll('\u00a0', ' ')).not.toMatch(/R\s?125[.,]00/)
    expect(host.textContent).toContain('Connect at least 3 red scarabs')
    expect(fetcher).not.toHaveBeenCalled()
  })

  it('ignores malformed receipts and receipts without a settled spin identity', async () => {
    await mount({ state: savedState(), feature: { ...paidTrail(), scarabTrailPoints: 999 }, spinKey: 4 })
    expect(host.textContent?.replaceAll('\u00a0', ' ')).not.toMatch(/R\s?125[.,]00/)
    await mount({ state: savedState(), feature: paidTrail(), spinKey: null })
    expect(host.textContent?.replaceAll('\u00a0', ' ')).not.toMatch(/R\s?125[.,]00/)
    expect(host.textContent).toContain('Connect at least 3 red scarabs')
  })
})
