// @vitest-environment jsdom
import { act } from 'react'
import { createRoot, type Root } from 'react-dom/client'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { CrookedRoadPanel, type CrookedRoadPanelProps } from '../src/CrookedRoadPanel'
import type { RoadBonus, RoadState } from '../src/road'

function snapshot(): RoadState {
  return {
    contractVersion: 1, revision: 17, board: Array(9).fill(null),
    hand: [{ id: 'ready', kind: 'straight', rotation: 0 }], queuedTiles: [],
    reserveTileCounts: { straight: 0, corner: 0, junction: 0 },
    funding: { spins: 4, requiredSpins: 20, wagerPointsTotal: 1200 },
    tickets: [], activeBonus: null, reachableDestinations: [], lastTravel: null, completedJourneys: 0,
  }
}
function bonus(kind: 'well' | 'fort' | 'hill'): RoadBonus {
  return { id: 'journey', kind, step: 0, totalSteps: kind === 'fort' ? 27 : 3, awardPoints: 0,
    fundedWagerPoints: 300, choice: null, board: null,
    held: kind === 'fort' ? Array(9).fill(0) : null, respinsRemaining: kind === 'fort' ? 3 : 0,
    doors: kind === 'hill' ? [0, 1, 2].map(index => ({ index, selected: false, revealed: false, prizePoints: null })) : [] }
}

let host: HTMLDivElement
let root: Root
let props: CrookedRoadPanelProps
async function render(state = snapshot()) {
  props = { ...props, state }
  await act(async () => root.render(<CrookedRoadPanel {...props} />))
}
const cell = (index: number) => host.querySelectorAll<HTMLButtonElement>('.crooked-road__cell')[index]!
async function click(button: HTMLButtonElement) { await act(async () => button.click()) }

beforeEach(() => {
  vi.stubGlobal('IS_REACT_ACT_ENVIRONMENT', true)
  host = document.createElement('div')
  document.body.append(host)
  root = createRoot(host)
  props = { state: null, feature: null, spinKey: 0, isSpinning: false, reducedMotion: true,
    pointValueInCents: 1, actionPending: false, error: null, onAction: vi.fn(async () => undefined) }
})
afterEach(() => {
  act(() => root.unmount())
  host.remove()
  vi.unstubAllGlobals()
})

describe('Crooked Road interaction boundaries', () => {
  it('sends a revisioned placement and waits for the server snapshot without an optimistic tile', async () => {
    let finish!: () => void
    props.onAction = vi.fn(() => new Promise<void>(resolve => { finish = resolve }))
    const state = snapshot()
    await render(state)
    await click(cell(7))
    expect(props.onAction).toHaveBeenCalledExactlyOnceWith({ expectedRevision: 17, action: 'place', handIndex: 0, cellIndex: 7, rotation: 0 })
    expect(cell(7).getAttribute('aria-label')).toContain('empty')
    expect(cell(7).disabled).toBe(true)
    await click(cell(7))
    expect(props.onAction).toHaveBeenCalledTimes(1)
    await act(async () => finish())
    expect(cell(7).getAttribute('aria-label')).toContain('empty')
    const board = [...state.board]
    board[7] = state.hand[0]!
    await render({ ...state, revision: 18, board, hand: [] })
    expect(cell(7).getAttribute('aria-label')).toContain('Straight facing north and south')
  })

  it('supports keyboard navigation, rotation and removal through server actions', async () => {
    const state = snapshot()
    const board = [...state.board]
    board[7] = { id: 'placed', kind: 'corner', rotation: 0 }
    await render({ ...state, board })
    cell(7).focus()
    await act(async () => cell(7).dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowUp', bubbles: true })))
    expect(document.activeElement).toBe(cell(4))
    await act(async () => cell(7).dispatchEvent(new KeyboardEvent('keydown', { key: 'r', bubbles: true })))
    expect(props.onAction).toHaveBeenLastCalledWith({ expectedRevision: 17, action: 'rotate', cellIndex: 7, rotation: 1 })
    await act(async () => cell(7).dispatchEvent(new KeyboardEvent('keydown', { key: 'Delete', bubbles: true })))
    expect(props.onAction).toHaveBeenLastCalledWith({ expectedRevision: 17, action: 'remove', cellIndex: 7 })
    expect(cell(7).getAttribute('aria-label')).toContain('Bend')
  })

  it('requires a real connection and a funded ticket before setting off', async () => {
    const state = snapshot()
    const board = [...state.board]
    for (const index of [7, 4, 1]) board[index] = { id: `tile-${index}`, kind: 'straight', rotation: 0 }
    const connected = { ...state, board, reachableDestinations: ['fort'] as const }
    await render(connected)
    const buttons = () => host.querySelectorAll<HTMLButtonElement>('.crooked-road__destinations button')
    expect(buttons()[1]!.disabled).toBe(true)
    await render({ ...connected, tickets: [{ id: 'ticket', fundedWagerPoints: 300 }] })
    expect(buttons()[0]!.disabled).toBe(true)
    expect(buttons()[1]!.disabled).toBe(false)
    expect(buttons()[2]!.disabled).toBe(true)
    await click(buttons()[1]!)
    expect(props.onAction).toHaveBeenCalledExactlyOnceWith({ expectedRevision: 17, action: 'set-off', destination: 'fort' })
  })

  it('uses distinct spring, hold-and-respin, and door controls without revealing hidden prizes', async () => {
    const state = snapshot()
    await render({ ...state, activeBonus: bonus('well') })
    const spring = host.querySelectorAll<HTMLButtonElement>('.crooked-road__spring-choices button')[1]!
    await click(spring)
    expect(props.onAction).toHaveBeenLastCalledWith({ expectedRevision: 17, action: 'choose-spring', choice: 1 })
    expect(spring.getAttribute('aria-pressed')).toBe('false')
    await render({ ...state, activeBonus: bonus('fort') })
    expect(host.querySelectorAll('.crooked-road__coin-cell')).toHaveLength(9)
    expect(host.querySelector('.crooked-road__respins')?.textContent).toContain('3respins left')
    expect(host.querySelector('.crooked-road__spring-choices')).toBeNull()
    await render({ ...state, activeBonus: bonus('hill') })
    const doors = host.querySelectorAll<HTMLButtonElement>('.crooked-road__doors button')
    expect(doors).toHaveLength(3)
    expect(host.querySelectorAll('.crooked-road__doors strong')).toHaveLength(0)
    await click(doors[2]!)
    expect(props.onAction).toHaveBeenLastCalledWith({ expectedRevision: 17, action: 'choose-door', choice: 2 })
  })

  it('holds a completed journey for review and returns to the map without a financial action', async () => {
    props.onPresentationBusyChange = vi.fn()
    props.feature = { kind: 'hill', completed: true,
      bonus: { ...bonus('hill'), step: 3, awardPoints: 900,
        doors: [0, 1, 2].map(index => ({ index, selected: index === 1, revealed: index === 1, prizePoints: index === 1 ? 300 : null })) } }
    await render()
    expect(host.querySelector('.crooked-road')?.getAttribute('data-feature-kind')).toBe('hill')
    expect(props.onPresentationBusyChange).toHaveBeenLastCalledWith(true)
    expect(host.querySelectorAll('.crooked-road__doors strong')).toHaveLength(1)
    await click(host.querySelector<HTMLButtonElement>('.crooked-road__return')!)
    expect(host.querySelector('.crooked-road')?.getAttribute('data-feature-kind')).toBeNull()
    expect(props.onPresentationBusyChange).toHaveBeenLastCalledWith(false)
    expect(props.onAction).not.toHaveBeenCalled()
  })
})
