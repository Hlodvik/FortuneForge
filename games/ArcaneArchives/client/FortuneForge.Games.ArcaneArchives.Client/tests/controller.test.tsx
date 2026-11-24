// @vitest-environment jsdom
import { act } from 'react'
import { createRoot, type Root } from 'react-dom/client'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { useArcaneArchives, type ArcaneArchivesController } from '../src/useArcaneArchives.js'
import type { ArcaneArchivesGameProps } from '../src/types.js'
import { potions, reply, spin, state, tome } from './fixtures.js'

const mocked = vi.hoisted(() => ({
  audio: { preferences: { volume: 65, mode: 'all' }, playCue: vi.fn(), playSequence: vi.fn(),
    startLoop: vi.fn(), stopLoop: vi.fn(), stopResultCues: vi.fn(), toggleMuted: vi.fn(), toggleResultsOnly: vi.fn(), setVolume: vi.fn() },
}))
vi.mock('@fortuneforge/slot-client-core', async importOriginal => ({
  ...await importOriginal<typeof import('@fortuneforge/slot-client-core')>(),
  useSlotAudio: () => mocked.audio,
  usePrefersReducedMotion: () => true,
  startSpinAnimation: (options: object) => ({ ...options, finished: false }),
  stopSpinAnimationWithCadence: async (options: { snapToTarget: () => void }) => options.snapToTarget(),
  finishSpinAnimation: vi.fn(), cancelSpinAnimation: vi.fn(), requestQuickStop: vi.fn(),
  waitForPresentation: async () => true,
}))

let root: Root
let host: HTMLDivElement
let c: ArcaneArchivesController
function Probe(props: ArcaneArchivesGameProps) { c = useArcaneArchives(props); return null }
const account = (id: string) => ({ userId: id, role: 'player', balances: { slotsCredits: 100 } })
async function flush() { await act(async () => { for (let index = 0; index < 8; index++) await Promise.resolve() }) }
async function mount(props: ArcaneArchivesGameProps) { await act(async () => root.render(<Probe {...props} />)); await flush() }
beforeEach(() => {
  Object.assign(globalThis, { IS_REACT_ACT_ENVIRONMENT: true })
  sessionStorage.clear(); host = document.createElement('div'); document.body.append(host); root = createRoot(host)
})
afterEach(async () => { await act(async () => root.unmount()); host.remove(); vi.clearAllMocks() })

describe('Arcane-owned controller', () => {
  it('uses the server balance and opens funding help when a stale wallet is rejected', async () => {
    const fetch = vi.fn().mockResolvedValueOnce(reply(state()))
      .mockResolvedValueOnce(reply({ code: 'insufficient-slot-credits', available: 1, required: 3,
        error: 'Insufficient slot credits.' }, true, 409))
    await mount({ account: account('arcane-insufficient'), authenticatedFetch: fetch })
    await act(async () => c.spin())
    expect(c.balance).toBe(1)
    expect(c.isReloadPromptOpen).toBe(true)
    expect(c.isSpinning).toBe(false)
    expect(c.isAutoSpinning).toBe(false)
    expect(c.hasCompletedSpin).toBe(false)
    expect(fetch).toHaveBeenCalledTimes(2)
    expect(fetch.mock.calls.filter(([, init]) => init?.method === 'POST')).toHaveLength(1)
  })
  it('restores persisted Tome and potion lock, then expires them solely from the settled server result', async () => {
    const bottled = potions()
    const active = potions({ active: { bottle: bottled.bottles[0], spinsRemaining: 1 }, bottles: [] })
    const loaded = state({ arcaneTome: tome({ activePage: 'embers', boostedSpinsRemaining: 1 }), arcanePotions: active })
    const result = spin({ wagerPoints: 20, chargedWagerCents: 500, slotsCreditsBalance: 95, arcaneTome: tome(), arcanePotions: potions({ bottles: [] }) })
    const fetch = vi.fn().mockResolvedValueOnce(reply(loaded)).mockResolvedValueOnce(reply(result))
    await mount({ account: account('arcane-expiry'), authenticatedFetch: fetch })
    expect(c.spinAvailability).toBe('available'); expect(c.activeWager).toBe(5); expect(c.preferredWager).toBe(3)
    expect(c.tomeState?.boostedSpinsRemaining).toBe(1)
    await act(async () => c.spin())
    expect(JSON.parse(fetch.mock.calls[1][1].body)).toMatchObject({ wagerPoints: 20, useFreeSpin: false })
    expect(c.tomeState?.activePage).toBeNull(); expect(c.potions?.active).toBeNull()
    expect(c.activeWager).toBe(3); expect(c.balance).toBe(95)
    expect(c.displayedReels).toEqual(result.reels); expect(c.tomeIngredientsAwarded).toEqual(result.arcaneIngredientsAwarded)
    expect(c.isSpinning).toBe(false); expect(c.hasCompletedSpin).toBe(true)
  })
  it('reloads authoritative workbench state when activation replies are lost, without issuing another command', async () => {
    const ready = state({ arcaneTome: tome({ grimoireReady: true }) })
    const after = state({ arcaneTome: tome({ activePage: 'embers', boostedSpinsRemaining: 5 }) })
    const fetch = vi.fn().mockResolvedValueOnce(reply(ready)).mockRejectedValueOnce(new TypeError('activation reply lost'))
      .mockResolvedValueOnce(reply(after))
    await mount({ account: account('arcane-command'), authenticatedFetch: fetch })
    expect(c.isTomeActivationRequired).toBe(true)
    await act(async () => c.changeTomePage('embers', true))
    expect(c.tomeState).toEqual(after.arcaneTome)
    expect(c.isTomeActivationRequired).toBe(false)
    expect(c.arcaneError).toContain('activation reply lost')
    expect(fetch.mock.calls.filter(([path]) => path === '/api/slots/arcane/tome')).toHaveLength(1)
  })
  it('serializes workbench commands and spins while waiting for authoritative state', async () => {
    let resolveCommand!: (value: Response) => void
    const fetch = vi.fn().mockResolvedValueOnce(reply(state())).mockImplementationOnce(() => new Promise<Response>(resolve => { resolveCommand = resolve }))
    await mount({ account: account('arcane-serialize'), authenticatedFetch: fetch })
    let pending!: Promise<void>
    await act(async () => { pending = c.activatePotion(7); await Promise.resolve() })
    expect(c.isArcaneUpdating).toBe(true)
    await act(async () => { await c.spin(); await c.changeTomePage('embers', false) })
    expect(fetch).toHaveBeenCalledTimes(2)
    const bottled = potions()
    await act(async () => { resolveCommand(reply(potions({ active: { bottle: bottled.bottles[0], spinsRemaining: 5 }, bottles: [] }))); await pending })
    expect(c.confirmedPotionUse).toBe(7); expect(c.isPotionWagerLocked).toBe(true); expect(c.isArcaneUpdating).toBe(false)
  })
  it('ignores a previous account’s late state and keeps keyboard mute available after button focus', async () => {
    let oldState!: (value: Response) => void
    const fetch = vi.fn().mockImplementationOnce(() => new Promise<Response>(resolve => { oldState = resolve }))
      .mockResolvedValueOnce(reply(state({ slotsCreditsBalance: 200 })))
    await mount({ account: account('arcane-old-owner'), authenticatedFetch: fetch })
    await mount({ account: account('arcane-new-owner'), authenticatedFetch: fetch })
    await act(async () => oldState(reply(state({ slotsCreditsBalance: 1 }))))
    await flush()
    expect(c.balance).toBe(200)
    const spinButton = document.createElement('button'); document.body.append(spinButton); spinButton.focus()
    await act(async () => { spinButton.dispatchEvent(new KeyboardEvent('keydown', { key: 'm', bubbles: true })) })
    expect(mocked.audio.toggleMuted).toHaveBeenCalledTimes(1)
    spinButton.remove()
  })
})
