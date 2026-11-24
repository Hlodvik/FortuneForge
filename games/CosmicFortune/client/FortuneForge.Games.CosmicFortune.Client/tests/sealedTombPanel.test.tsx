// @vitest-environment jsdom
import { act } from 'react'
import { createRoot, type Root } from 'react-dom/client'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { SealedTombPanel, type SealedTombPanelProps } from '../src/SealedTombPanel'
import { slotState, ticketId, tombRun, tombState } from './fixtures'

let host: HTMLDivElement
let root: Root
let props: SealedTombPanelProps
async function render() { await act(async () => root.render(<SealedTombPanel {...props} />)) }
const namedButton = (label: string) => [...host.querySelectorAll<HTMLButtonElement>('button')]
  .find(button => button.getAttribute('aria-label') === label || button.textContent?.includes(label))!
beforeEach(() => {
  vi.stubGlobal('IS_REACT_ACT_ENVIRONMENT', true)
  host = document.createElement('div'); document.body.append(host); root = createRoot(host)
  props = { state: slotState().gameFeatureState, pointValueInCents: 25, reducedMotion: true,
    isSpinning: false, actionPending: false, onAction: vi.fn(async () => {}) }
})
afterEach(async () => { await act(async () => root.unmount()); host.remove(); vi.unstubAllGlobals() })

describe('The Sealed Tomb expedition', () => {
  it('shows the server-pinned scarab requirement without changing saved sigil progress', async () => {
    await render()
    expect(host.textContent).toContain('3+ red scarabs on a paid spin add a sigil')
    const legacy = tombState(5)
    props.state = slotState(5, { ...legacy, funding: { ...legacy.funding, minimumScarabs: 2,
      sigils: 5, averageWagerPoints: 12 } }).gameFeatureState
    await render()
    expect(host.textContent).toContain('2+ red scarabs on a paid spin add a sigil')
    expect(host.textContent).toContain('5 / 6 sigils')
    expect(props.onAction).not.toHaveBeenCalled()
  })

  it('previews the actual pyramid and requires a funded ticket before entry', async () => {
    await render()
    expect(host.querySelectorAll('.sealed-tomb__floor')).toHaveLength(6)
    expect(namedButton('Earn an expedition ticket').disabled).toBe(true)
    expect(host.querySelectorAll('.sealed-tomb__room--choice')).toHaveLength(0)
    props.state = slotState(8, { ...tombState(8), tickets: [{ id: ticketId, fundedWagerPoints: 12 }] }).gameFeatureState
    await render()
    await act(async () => namedButton('Enter the Sealed Tomb').click())
    expect(props.onAction).toHaveBeenCalledExactlyOnceWith({ action: 'enter', expectedRevision: 8 })
    expect(host.querySelector('[data-expedition-active]')).toBeNull()
  })

  it('places three real server-offered room choices inside the pyramid', async () => {
    props.state = slotState(4, { ...tombState(4), activeRun: tombRun() }).gameFeatureState
    await render()
    expect(host.querySelectorAll('.sealed-tomb__map button')).toHaveLength(3)
    expect(host.querySelector('[data-expedition-active="true"]')).not.toBeNull()
    expect(host.textContent).toContain('Sun relic')
    expect(host.textContent).toContain('Three keys open the sarcophagus')
    await act(async () => namedButton('Open Sun relic').click())
    expect(props.onAction).toHaveBeenCalledExactlyOnceWith({ action: 'open-room', expectedRevision: 4, choiceId: tombRun().choices[1].id })
    expect(host.textContent).toContain('6 / 6')
    expect(host.querySelectorAll('[data-visited]')).toHaveLength(0)
  })

  it('keeps a ticket saved while previously earned reel spins are being completed', async () => {
    props.state = { ...slotState(4, { ...tombState(4), tickets: [{ id: ticketId, fundedWagerPoints: 12 }] }).gameFeatureState,
      freeSpinsRemaining: 6, freeSpinWagerPoints: 12 }
    await render()
    expect(namedButton('Finish your earned reel spins first').disabled).toBe(true)
    await act(async () => namedButton('Finish your earned reel spins first').click())
    expect(props.onAction).not.toHaveBeenCalled()
  })

  it.each(['disabled', 'isSpinning', 'actionPending'] as const)('blocks room and collection actions while %s', async property => {
    props = { ...props, [property]: true, state: slotState(4, { ...tombState(4), activeRun: tombRun() }).gameFeatureState }
    await render()
    await act(async () => { namedButton('Open Golden hoard').click(); namedButton('Collect').click() })
    expect(props.onAction).not.toHaveBeenCalled()
    expect(namedButton('Open Golden hoard').disabled).toBe(true)
  })

  it('prevents duplicate room actions and shows confirmed progress only', async () => {
    let finish!: () => void
    props.onAction = vi.fn(() => new Promise<void>(resolve => { finish = resolve }))
    props.state = slotState(4, { ...tombState(4), activeRun: tombRun() }).gameFeatureState
    await render()
    await act(async () => { namedButton('Open Hidden key').click(); namedButton('Open Hidden key').click() })
    expect(props.onAction).toHaveBeenCalledTimes(1)
    expect(host.textContent).toContain('0 / 3')
    props.state = slotState(5, { ...tombState(5), activeRun: { ...tombRun(), torchesRemaining: 5, keys: 1,
      roomPoints: 12, bankedPoints: 72, path: [{ step: 1, kind: 'key', relic: null, awardPoints: 12 }] } }).gameFeatureState
    await act(async () => finish())
    await render()
    expect(host.textContent).toContain('1 / 3')
    expect(host.querySelectorAll('[data-visited]')).toHaveLength(1)
    expect(host.textContent).toContain('Last discovery')
  })

  it('shows a failure without inventing a reward or consuming a torch', async () => {
    props.onAction = vi.fn(async () => { throw new Error('Connection interrupted') })
    props.state = slotState(4, { ...tombState(4), activeRun: tombRun() }).gameFeatureState
    await render()
    await act(async () => namedButton('Open Golden hoard').click())
    expect(host.querySelector('[role="alert"]')?.textContent).toBe('Connection interrupted')
    expect(host.textContent).toContain('6 / 6')
    expect(host.querySelectorAll('[data-visited]')).toHaveLength(0)
  })

  it('retries a saved action exactly and blocks every new choice while it is pending', async () => {
    const pendingAction = { action: 'collect', expectedRevision: 4, choiceId: null } as const
    props.state = slotState(4, { ...tombState(4), activeRun: tombRun(), pendingAction }).gameFeatureState
    await render()
    expect(namedButton('Open Hidden key').disabled).toBe(true)
    await act(async () => namedButton('Resume saved action').click())
    expect(props.onAction).toHaveBeenCalledExactlyOnceWith(pendingAction)
  })

  it('replaces rooms with the unlocked sarcophagus and never supplies its prize locally', async () => {
    props.state = slotState(7, { ...tombState(7), activeRun: { ...tombRun(), keys: 3, torchesRemaining: 3,
      phase: 'finale', choices: [], roomPoints: 36, bankedPoints: 96,
      path: [1, 2, 3].map(step => ({ step, kind: 'key', relic: null, awardPoints: 12 })) } }).gameFeatureState
    await render()
    expect(host.querySelectorAll('.sealed-tomb__map button')).toHaveLength(0)
    expect(namedButton('Open the sarcophagus').disabled).toBe(false)
    await act(async () => namedButton('Open the sarcophagus').click())
    expect(props.onAction).toHaveBeenCalledExactlyOnceWith({ action: 'open-sarcophagus', expectedRevision: 7 })
    expect(host.querySelector('.sealed-tomb__completion')).toBeNull()
  })

  it('keeps a settled result visible until server-confirmed dismissal', async () => {
    props.state = slotState(9, { ...tombState(9), lastRun: { id: ticketId, fundedWagerPoints: 12, roomsOpened: 0,
      keys: 0, relics: { sun: 0, moon: 0, serpent: 0 }, relicMultiplier: 1,
      basePoints: 60, roomPoints: 0, finalePoints: 0, payoutPoints: 60, reason: 'collect' } }).gameFeatureState
    await render()
    expect(host.textContent).toContain('Expedition complete')
    await act(async () => namedButton('Return to the reels').click())
    expect(props.onAction).toHaveBeenCalledExactlyOnceWith({ action: 'dismiss', expectedRevision: 9 })
    expect(host.querySelector('[data-expedition-active="true"]')).not.toBeNull()
    props.state = slotState(10).gameFeatureState
    await render()
    expect(host.querySelector('[data-expedition-active]')).toBeNull()
    expect(host.querySelector('.sealed-tomb__completion')).toBeNull()
  })
})
