// @vitest-environment jsdom
import { act, cleanup, fireEvent, render, screen, within } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { DragonTigerGame } from './DragonTigerGame'
import { DragonTigerGatewayError, type DragonTigerCard, type DragonTigerGateway, type DragonTigerRound, type DragonTigerStatus } from './contracts'
import { playDragonTigerAudio } from './dragonTigerAudio'

vi.mock('./dragonTigerAudio', () => ({ playDragonTigerAudio: vi.fn() }))

const status: DragonTigerStatus = { available: true, minimumStake: .5, maximumStake: 250, maximumTotalStake: 500, stakeIncrement: .5, balance: 100, mode: 'practice' }
const dragonWin: DragonTigerRound = {
  roundId: 'client-round-1', phase: 'settled', balance: 102,
  dragonCard: { rank: 'king', suit: 'hearts' }, tigerCard: { rank: 'ace', suit: 'spades' },
  dragonValue: 13, tigerValue: 1, outcome: 'dragon',
  bets: [
    { side: 'dragon', stake: 2, disposition: 'win', profit: 2, totalReturn: 4 },
    { side: 'tiger', stake: 0, disposition: 'not-placed', profit: 0, totalReturn: 0 },
    { side: 'tie', stake: 0, disposition: 'not-placed', profit: 0, totalReturn: 0 },
  ], totalStake: 2, profit: 2, totalReturn: 4, shoeCardsUsed: 2, shoeCardsRemaining: 414,
}
function makeGateway(round: DragonTigerRound = dragonWin) {
  return {
    getStatus: vi.fn<DragonTigerGateway['getStatus']>().mockResolvedValue(status),
    getLatestRound: vi.fn<NonNullable<DragonTigerGateway['getLatestRound']>>().mockResolvedValue(null),
    createRound: vi.fn<DragonTigerGateway['createRound']>().mockResolvedValue(round),
  }
}
const spot = (side: string) => screen.getByRole('button', { name: new RegExp(`^Bet on ${side},`) })
const chipPicker = () => screen.getByRole('button', { name: /^Choose chip value, currently R/ })
function selectChip(value: number) {
  if (!screen.queryByRole('group', { name: 'Choose chip value' })) fireEvent.click(chipPicker())
  fireEvent.click(screen.getByRole('button', { name: `R${value.toFixed(2)} chip` }))
}
const takeBack = (side: string) => screen.getByRole('button', { name: `Take back last chip from ${side} bet` })
const resetChips = () => screen.getByRole('button', { name: 'Reset chips on board' })
function expectTakeBackBlocked(side: string, click = true) {
  const button = screen.queryByRole('button', { name: `Take back last chip from ${side} bet` }) as HTMLButtonElement | null
  expect(button === null || button.disabled).toBe(true)
  if (button && click) fireEvent.click(button)
}
const total = () => screen.getByTestId('dragon-tiger-total-bet').textContent
const balance = () => screen.getByTestId('dragon-tiger-balance').textContent
const win = () => screen.getByTestId('dragon-tiger-win').textContent
const flush = async () => { await act(async () => { await Promise.resolve(); await Promise.resolve() }) }
const advance = async (ms: number) => { await act(async () => { vi.advanceTimersByTime(ms) }) }
let componentSize = { width: 900, height: 700 }
const resizeObservers = new Set<{ callback: ResizeObserverCallback; targets: Set<Element>; observer: ResizeObserver }>()
function resizeEntry(target: Element): ResizeObserverEntry {
  const { width, height } = componentSize
  const size = [{ inlineSize: width, blockSize: height }]
  return {
    target, contentRect: { x: 0, y: 0, top: 0, left: 0, right: width, bottom: height, width, height, toJSON: () => ({ width, height }) },
    borderBoxSize: size, contentBoxSize: size, devicePixelContentBoxSize: size,
  }
}
function resizeComponent(width: number, height: number) {
  act(() => {
    componentSize = { width, height }
    for (const item of resizeObservers) item.callback(Array.from(item.targets, resizeEntry), item.observer)
  })
}
async function start(gateway = makeGateway(), playerId = 'player-a', onBalanceChange?: (balance: number) => void) {
  const view = render(<DragonTigerGame gateway={gateway} playerId={playerId} onBalanceChange={onBalanceChange}/>)
  await flush()
  return view
}

beforeEach(() => {
  localStorage.clear()
  vi.clearAllMocks()
  vi.useFakeTimers()
  vi.stubGlobal('matchMedia', vi.fn().mockReturnValue({ matches: false }))
  componentSize = { width: 900, height: 700 }
  resizeObservers.clear()
  vi.stubGlobal('ResizeObserver', class implements ResizeObserver {
    private item: { callback: ResizeObserverCallback; targets: Set<Element>; observer: ResizeObserver }
    constructor(callback: ResizeObserverCallback) {
      this.item = { callback, targets: new Set(), observer: this }
      resizeObservers.add(this.item)
    }
    observe(target: Element) { this.item.targets.add(target); this.item.callback([resizeEntry(target)], this) }
    unobserve(target: Element) { this.item.targets.delete(target) }
    disconnect() { this.item.targets.clear(); resizeObservers.delete(this.item) }
  })
})
afterEach(() => { cleanup(); vi.useRealTimers(); vi.restoreAllMocks(); vi.unstubAllGlobals() })

describe('DragonTigerGame betting and results', () => {
  it('keeps every chip denomination accessible while displaying only the numbered PNG artwork', async () => {
    await start()
    expect(screen.queryByRole('complementary', { name: 'Recent results' })).toBeNull()
    expect(screen.queryByText('Your results will appear here')).toBeNull()
    expect(screen.queryByRole('group', { name: 'Choose chip value' })).toBeNull()
    expect(chipPicker().childNodes).toHaveLength(1)
    expect(chipPicker().firstElementChild?.tagName).toBe('IMG')
    expect(chipPicker().textContent).toBe('')
    expect(screen.getByRole('button', { name: 'Deal cards' }).textContent).toBe('DEAL')
    fireEvent.click(chipPicker())
    const selector = screen.getByRole('group', { name: 'Choose chip value' })
    expect(within(selector).getAllByRole('button', { name: /^R[\d.]+ chip$/ })).toHaveLength(7)
    for (const value of [.5, 1, 2, 5, 10, 25, 50]) {
      const button = within(selector).getByRole('button', { name: `R${value.toFixed(2)} chip` })
      expect(button.childNodes).toHaveLength(1)
      expect(button.firstElementChild?.tagName).toBe('IMG')
      expect(button.textContent).toBe('')
      expect(button.querySelector('span')).toBeNull()
      expect(button.querySelector('img')?.getAttribute('src')).toContain(`chip-${value === .5 ? '050' : value}.png`)
    }
  })

  it('steps the selected chip up and down through every denomination without exceeding either bound or changing bets', async () => {
    const gateway = makeGateway()
    await start(gateway)
    const lower = screen.getByRole('button', { name: 'Decrease chip value' })
    const higher = screen.getByRole('button', { name: 'Increase chip value' })
    expect(chipPicker().getAttribute('aria-label')).toBe('Choose chip value, currently R1.00')
    fireEvent.click(lower)
    expect(chipPicker().getAttribute('aria-label')).toBe('Choose chip value, currently R0.50')
    expect(lower).toHaveProperty('disabled', true)
    fireEvent.click(lower)
    expect(chipPicker().getAttribute('aria-label')).toBe('Choose chip value, currently R0.50')
    for (const value of [1, 2, 5, 10, 25, 50]) {
      expect(higher).toHaveProperty('disabled', false)
      fireEvent.click(higher)
      expect(chipPicker().getAttribute('aria-label')).toBe(`Choose chip value, currently R${value.toFixed(2)}`)
    }
    expect(higher).toHaveProperty('disabled', true)
    fireEvent.click(higher)
    expect(chipPicker().getAttribute('aria-label')).toBe('Choose chip value, currently R50.00')
    expect(lower).toHaveProperty('disabled', false)
    expect(screen.queryByRole('group', { name: 'Choose chip value' })).toBeNull()
    expect(total()).toBe('R0.00')
    expect(balance()).toBe('R100.00')
    expect(gateway.createRound).not.toHaveBeenCalled()
  })

  it('opens the chip options only on request and closes them on selection, Escape or an outside pointer', async () => {
    await start()
    expect(chipPicker().getAttribute('aria-expanded')).toBe('false')
    fireEvent.click(chipPicker())
    expect(chipPicker().getAttribute('aria-expanded')).toBe('true')
    expect(screen.getByRole('group', { name: 'Choose chip value' })).toBeTruthy()
    selectChip(25)
    expect(chipPicker().getAttribute('aria-label')).toBe('Choose chip value, currently R25.00')
    expect(chipPicker().getAttribute('aria-expanded')).toBe('false')
    expect(screen.queryByRole('group', { name: 'Choose chip value' })).toBeNull()
    fireEvent.click(chipPicker())
    fireEvent.keyDown(document, { key: 'Escape' })
    expect(screen.queryByRole('group', { name: 'Choose chip value' })).toBeNull()
    fireEvent.click(chipPicker())
    fireEvent.pointerDown(document.body)
    expect(screen.queryByRole('group', { name: 'Choose chip value' })).toBeNull()
    expect(chipPicker().getAttribute('aria-label')).toBe('Choose chip value, currently R25.00')
  })

  it('adds chips with repeated taps and returns chips from one spot without changing the other spots or wallet', async () => {
    const gateway = makeGateway()
    await start(gateway)
    expect(total()).toBe('R0.00')
    expect(balance()).toBe('R100.00')
    expect(win()).toBe('R0.00')
    selectChip(5)
    fireEvent.click(spot('Dragon')); fireEvent.click(spot('Dragon')); fireEvent.click(spot('Dragon'))
    expect(spot('Dragon').getAttribute('aria-label')).toContain('current bet R15.00')
    expect(within(spot('Dragon').parentElement!).getByLabelText('3 chips placed').querySelectorAll('img')).toHaveLength(3)
    expect(spot('Dragon').querySelector('button')).toBeNull()
    expect(spot('Dragon').parentElement!.contains(takeBack('Dragon'))).toBe(true)
    selectChip(2)
    fireEvent.click(spot('Tiger'))
    selectChip(.5)
    fireEvent.click(spot('Tie'))
    expect(total()).toBe('R17.50')
    fireEvent.click(takeBack('Dragon'))
    expect(total()).toBe('R12.50')
    expect(spot('Dragon').getAttribute('aria-label')).toContain('current bet R10.00')
    expect(spot('Tiger').getAttribute('aria-label')).toContain('current bet R2.00')
    expect(spot('Tie').getAttribute('aria-label')).toContain('current bet R0.50')
    fireEvent.click(takeBack('Tie'))
    expect(total()).toBe('R12.00')
    expect(spot('Tie').getAttribute('aria-label')).toContain('current bet R0.00')
    fireEvent.click(takeBack('Tiger'))
    fireEvent.click(takeBack('Dragon')); fireEvent.click(takeBack('Dragon'))
    expect(total()).toBe('R0.00')
    expect(screen.getByRole('button', { name: /Deal cards/ })).toHaveProperty('disabled', true)
    expect(screen.queryByRole('button', { name: /Undo|Clear/ })).toBeNull()
    expect(balance()).toBe('R100.00')
    expect(gateway.createRound).not.toHaveBeenCalled()
  })

  it('takes back the top denomination from the chosen spot rather than the last globally placed chip', async () => {
    const gateway = makeGateway()
    await start(gateway)
    fireEvent.click(spot('Dragon'))
    selectChip(5); fireEvent.click(spot('Dragon'))
    selectChip(2); fireEvent.click(spot('Tiger'))
    expect(total()).toBe('R8.00')
    fireEvent.click(takeBack('Dragon'))
    expect(total()).toBe('R3.00')
    expect(spot('Dragon').getAttribute('aria-label')).toContain('current bet R1.00')
    expect(spot('Tiger').getAttribute('aria-label')).toContain('current bet R2.00')
    expect(within(spot('Dragon').parentElement!).getByLabelText('1 chips placed').querySelector('img')?.getAttribute('src')).toContain('chip-1.png')
    expect(balance()).toBe('R100.00')
    expect(gateway.createRound).not.toHaveBeenCalled()
  })

  it('grows a vertical chip stack per placement and only its chip-return button takes back one chip', async () => {
    const gateway = makeGateway(), changed = vi.fn()
    await start(gateway, 'player-a', changed)
    const dragonArea = spot('Dragon').parentElement!
    for (const [index, value] of [1, 5, 2].entries()) {
      selectChip(value)
      fireEvent.click(spot('Dragon'))
      const stack = within(dragonArea).getByLabelText(`${index + 1} chips placed`)
      expect(stack.querySelectorAll('img')).toHaveLength(index + 1)
      const pile = stack.parentElement!
      const placed = pile.closest<HTMLElement>('.ff-dt__placed-chips')!
      expect(placed.tagName).toBe('DIV')
      expect(placed.classList.contains('ff-dt__placed-chips')).toBe(true)
      expect(pile.classList.contains('ff-dt__chip-pile')).toBe(true)
      expect(pile.style.getPropertyValue('--dt-stack-depth')).toBe(String(index))
      expect(takeBack('Dragon').parentElement).toBe(pile)
      expect(takeBack('Dragon').classList.contains('ff-dt__chip-return')).toBe(true)
      expect(takeBack('Dragon').children).toHaveLength(0)
      expect(placed.querySelector('b')?.closest('button')).toBeNull()
      expect(stack.closest('button')).toBeNull()
    }
    const stack = within(dragonArea).getByLabelText('3 chips placed')
    expect(Array.from(stack.querySelectorAll('img'), image => image.style.getPropertyValue('--dt-stack-index'))).toEqual(['2', '1', '0'])
    expect(stack.querySelector('img:last-child')?.getAttribute('src')).toContain('chip-2.png')
    selectChip(.5); fireEvent.click(spot('Tiger'))
    expect(total()).toBe('R8.50')
    fireEvent.click(stack.closest('.ff-dt__placed-chips')!.querySelector('b')!)
    expect(total()).toBe('R8.50')
    expect(within(dragonArea).getByLabelText('3 chips placed').querySelectorAll('img')).toHaveLength(3)
    fireEvent.click(takeBack('Dragon'))
    expect(total()).toBe('R6.50')
    expect(spot('Dragon').getAttribute('aria-label')).toContain('current bet R6.00')
    expect(spot('Tiger').getAttribute('aria-label')).toContain('current bet R0.50')
    expect(within(dragonArea).getByLabelText('2 chips placed').querySelectorAll('img')).toHaveLength(2)
    expect(within(dragonArea).getByLabelText('2 chips placed').parentElement!.style.getPropertyValue('--dt-stack-depth')).toBe('1')
    expect(balance()).toBe('R100.00')
    expect(changed).not.toHaveBeenCalled()
    expect(gateway.createRound).not.toHaveBeenCalled()
  })

  it('caps the displayed stack at six chips without losing the seventh placement or debiting the wallet', async () => {
    const gateway = makeGateway(), changed = vi.fn()
    await start(gateway, 'player-a', changed)
    for (let index = 0; index < 7; index++) fireEvent.click(spot('Dragon'))
    const dragonArea = spot('Dragon').parentElement!
    const stack = within(dragonArea).getByLabelText('7 chips placed')
    expect(stack.querySelectorAll('img')).toHaveLength(6)
    expect(stack.querySelector('em')?.textContent).toBe('+1')
    expect(stack.parentElement!.style.getPropertyValue('--dt-stack-depth')).toBe('5')
    expect(Array.from(stack.querySelectorAll('img'), image => image.style.getPropertyValue('--dt-stack-index'))).toEqual(['5', '4', '3', '2', '1', '0'])
    expect(total()).toBe('R7.00')
    expect(spot('Dragon').getAttribute('aria-label')).toContain('current bet R7.00')
    fireEvent.click(takeBack('Dragon'))
    const remaining = within(dragonArea).getByLabelText('6 chips placed')
    expect(remaining.querySelectorAll('img')).toHaveLength(6)
    expect(remaining.querySelector('em')).toBeNull()
    expect(total()).toBe('R6.00')
    expect(balance()).toBe('R100.00')
    expect(changed).not.toHaveBeenCalled()
    expect(gateway.createRound).not.toHaveBeenCalled()
  })

  it.each([
    { width: 600, height: 700, mode: 'phone', images: 1, overflow: '+6', piles: 1 },
    { width: 601, height: 700, mode: 'tablet', images: 6, overflow: '+1', piles: 1 },
    { width: 1099, height: 700, mode: 'tablet', images: 6, overflow: '+1', piles: 1 },
    { width: 1100, height: 700, mode: 'expanded', images: 7, overflow: null, piles: 2 },
    { width: 960, height: 500, mode: 'phone', images: 1, overflow: '+6', piles: 1 },
    { width: 961, height: 500, mode: 'tablet', images: 6, overflow: '+1', piles: 1 },
    { width: 1100, height: 500, mode: 'expanded', images: 7, overflow: null, piles: 2 },
  ])('renders $mode chip piles at component size $width×$height without changing the wager', async ({ width, height, mode, images, overflow, piles }) => {
    const gateway = makeGateway()
    await start(gateway)
    resizeComponent(width, height)
    for (let index = 0; index < 7; index++) fireEvent.click(spot('Dragon'))
    const placed = spot('Dragon').parentElement!.querySelector<HTMLElement>('.ff-dt__placed-chips')!
    expect(placed.dataset.chipMode).toBe(mode)
    expect(placed.dataset.chipCount).toBe('7')
    expect(placed.querySelectorAll('img')).toHaveLength(images)
    expect(placed.querySelectorAll('.ff-dt__chip-pile')).toHaveLength(piles)
    expect(placed.querySelector('em')?.textContent ?? null).toBe(overflow)
    expect(total()).toBe('R7.00')
    expect(balance()).toBe('R100.00')
    expect(gateway.createRound).not.toHaveBeenCalled()
  })

  it('resizes the same placements and returns an earlier desktop pile’s actual top denomination', async () => {
    const gateway = makeGateway(), changed = vi.fn()
    await start(gateway, 'player-a', changed)
    resizeComponent(390, 844)
    for (let index = 0; index < 5; index++) fireEvent.click(spot('Dragon'))
    selectChip(.5); fireEvent.click(spot('Tiger'))
    selectChip(2); fireEvent.click(spot('Dragon'))
    selectChip(5); fireEvent.click(spot('Dragon'))
    const dragonArea = spot('Dragon').parentElement!
    const placed = () => dragonArea.querySelector<HTMLElement>('.ff-dt__placed-chips')!
    expect(placed().dataset.chipMode).toBe('phone')
    expect(placed().querySelectorAll('img')).toHaveLength(1)
    expect(placed().querySelector('img')?.getAttribute('src')).toContain('chip-5.png')
    expect(placed().querySelector('em')?.textContent).toBe('+6')
    resizeComponent(900, 700)
    expect(placed().dataset.chipMode).toBe('tablet')
    expect(placed().querySelectorAll('img')).toHaveLength(6)
    expect(placed().querySelector('em')?.textContent).toBe('+1')
    resizeComponent(1300, 800)
    expect(placed().dataset.chipMode).toBe('expanded')
    expect(placed().querySelectorAll('.ff-dt__chip-pile')).toHaveLength(2)
    expect(placed().querySelectorAll('img')).toHaveLength(7)
    expect(placed().querySelector('em')).toBeNull()
    expect(total()).toBe('R12.50')
    fireEvent.click(screen.getByRole('button', { name: 'Take back top chip from Dragon stack 1' }))
    expect(total()).toBe('R10.50')
    expect(spot('Dragon').getAttribute('aria-label')).toContain('current bet R10.00')
    expect(spot('Tiger').getAttribute('aria-label')).toContain('current bet R0.50')
    expect(placed().querySelectorAll('img')).toHaveLength(6)
    expect(placed().querySelector('img:last-child')?.getAttribute('src')).toContain('chip-5.png')
    fireEvent.click(takeBack('Dragon'))
    expect(total()).toBe('R5.50')
    expect(placed().querySelectorAll('img')).toHaveLength(5)
    resizeComponent(390, 844)
    expect(placed().querySelectorAll('img')).toHaveLength(1)
    expect(placed().querySelector('em')?.textContent).toBe('+4')
    expect(balance()).toBe('R100.00')
    expect(changed).not.toHaveBeenCalled()
    expect(gateway.createRound).not.toHaveBeenCalled()
  })

  it.each([{ height: 800, maximumPiles: 6 }, { height: 500, maximumPiles: 3 }])('represents every chip in a large desktop wager within $maximumPiles piles at height $height', async ({ height, maximumPiles }) => {
    const gateway = makeGateway()
    await start(gateway)
    resizeComponent(1300, height)
    const dragonSpot = spot('Dragon')
    for (let index = 0; index < 37; index++) fireEvent.click(dragonSpot)
    const placed = spot('Dragon').parentElement!.querySelector<HTMLElement>('.ff-dt__placed-chips')!
    expect(placed.dataset.chipMode).toBe('expanded')
    expect(placed.dataset.chipCount).toBe('37')
    expect(placed.querySelectorAll('img')).toHaveLength(37)
    expect(placed.querySelectorAll('.ff-dt__chip-pile')).toHaveLength(maximumPiles)
    expect(placed.querySelector('em')).toBeNull()
    expect(total()).toBe('R37.00')
    expect(balance()).toBe('R100.00')
    expect(gateway.createRound).not.toHaveBeenCalled()
  }, 15_000)

  it('preserves a pending deal and its original key while responsive chip layouts change', async () => {
    const round: DragonTigerRound = {
      ...dragonWin, roundId: 'responsive-pending', totalStake: 7, totalReturn: 14, profit: 7, balance: 107,
      bets: dragonWin.bets.map(bet => bet.side === 'dragon' ? { ...bet, stake: 7, totalReturn: 14, profit: 7 } : bet),
    }
    let resolve!: (round: DragonTigerRound) => void
    const gateway = makeGateway(round), changed = vi.fn()
    gateway.createRound.mockReturnValueOnce(new Promise<DragonTigerRound>(done => { resolve = done }))
    await start(gateway, 'player-a', changed)
    for (let index = 0; index < 7; index++) fireEvent.click(spot('Dragon'))
    fireEvent.click(screen.getByRole('button', { name: 'Deal cards' })); await flush()
    const stored = localStorage.getItem('fortuneforge:dragon-tiger:pending:player-a')
    expect(stored).not.toBeNull()
    for (const [width, height] of [[390, 844], [1300, 800], [900, 700]]) {
      resizeComponent(width, height)
      expectTakeBackBlocked('Dragon')
      expect(total()).toBe('R7.00')
      expect(balance()).toBe('R100.00')
      expect(localStorage.getItem('fortuneforge:dragon-tiger:pending:player-a')).toBe(stored)
      expect(gateway.createRound).toHaveBeenCalledOnce()
    }
    await act(async () => { resolve(round); await Promise.resolve() })
    await advance(1700)
    expect(balance()).toBe('R107.00')
    expect(win()).toBe('R7.00')
    expect(changed).toHaveBeenCalledExactlyOnceWith(107)
    expect(gateway.createRound.mock.calls[0][0]).toEqual({ dragonStake: 7, tigerStake: 0, tieStake: 0 })
    expect(gateway.createRound.mock.calls[0][1].idempotencyKey).toBe(JSON.parse(stored!).key)
    expect(localStorage.getItem('fortuneforge:dragon-tiger:pending:player-a')).toBeNull()
  })

  it('resets every draft spot without changing the selected chip, wallet, or requesting a deal', async () => {
    const gateway = makeGateway(), changed = vi.fn()
    await start(gateway, 'player-a', changed)
    expect(resetChips()).toHaveProperty('disabled', true)
    selectChip(5)
    fireEvent.click(spot('Dragon')); fireEvent.click(spot('Dragon')); fireEvent.click(spot('Tiger'))
    selectChip(.5); fireEvent.click(spot('Tie'))
    expect(total()).toBe('R15.50')
    expect(resetChips()).toHaveProperty('disabled', false)
    vi.mocked(playDragonTigerAudio).mockClear()
    fireEvent.click(resetChips())
    expect(total()).toBe('R0.00')
    for (const side of ['Dragon', 'Tiger', 'Tie']) {
      expect(spot(side).getAttribute('aria-label')).toContain('current bet R0.00')
      expect(screen.queryByRole('button', { name: `Take back last chip from ${side} bet` })).toBeNull()
    }
    expect(chipPicker().getAttribute('aria-label')).toBe('Choose chip value, currently R0.50')
    expect(resetChips()).toHaveProperty('disabled', true)
    expect(screen.getByRole('button', { name: 'Deal cards' })).toHaveProperty('disabled', true)
    expect(screen.getAllByLabelText('Face down card')).toHaveLength(2)
    expect(balance()).toBe('R100.00')
    expect(changed).not.toHaveBeenCalled()
    expect(gateway.createRound).not.toHaveBeenCalled()
    expect(localStorage.getItem('fortuneforge:dragon-tiger:pending:player-a')).toBeNull()
    expect(playDragonTigerAudio).toHaveBeenCalledWith('chip', false)
  })

  it('keeps the previous round available to Rebet after resetting chips', async () => {
    const gateway = makeGateway(), changed = vi.fn()
    gateway.getLatestRound.mockResolvedValueOnce(dragonWin)
    await start(gateway, 'player-a', changed)
    expect(resetChips()).toHaveProperty('disabled', true)
    fireEvent.keyDown(document, { key: 'Escape' })
    selectChip(5)
    fireEvent.click(screen.getByRole('button', { name: /Rebet/ }))
    fireEvent.click(spot('Tiger'))
    expect(total()).toBe('R7.00')
    fireEvent.click(resetChips())
    expect(total()).toBe('R0.00')
    expect(chipPicker().getAttribute('aria-label')).toBe('Choose chip value, currently R5.00')
    expect(screen.getByRole('button', { name: /Rebet/ })).toHaveProperty('disabled', false)
    expect(balance()).toBe('R102.00')
    fireEvent.click(screen.getByRole('button', { name: /Rebet/ }))
    expect(total()).toBe('R2.00')
    expect(spot('Dragon').getAttribute('aria-label')).toContain('current bet R2.00')
    expect(spot('Tiger').getAttribute('aria-label')).toContain('current bet R0.00')
    expect(gateway.createRound).not.toHaveBeenCalled()
    expect(changed).not.toHaveBeenCalled()
  })

  it('cannot return or reset chips while a request is pending, during the reveal, or from a settled wager', async () => {
    let resolve!: (round: DragonTigerRound) => void
    const delayed = new Promise<DragonTigerRound>(done => { resolve = done })
    const gateway = makeGateway()
    gateway.createRound.mockReturnValueOnce(delayed)
    await start(gateway)
    fireEvent.click(spot('Dragon')); fireEvent.click(spot('Dragon'))
    fireEvent.click(screen.getByRole('button', { name: /Deal cards/ })); await flush()
    expect(chipPicker()).toHaveProperty('disabled', true)
    expect(screen.getByRole('button', { name: 'Increase chip value' })).toHaveProperty('disabled', true)
    expect(screen.getByRole('button', { name: 'Decrease chip value' })).toHaveProperty('disabled', true)
    fireEvent.click(chipPicker())
    expect(screen.queryByRole('group', { name: 'Choose chip value' })).toBeNull()
    const stored = localStorage.getItem('fortuneforge:dragon-tiger:pending:player-a')
    expect(resetChips()).toHaveProperty('disabled', true)
    fireEvent.click(resetChips())
    expect(localStorage.getItem('fortuneforge:dragon-tiger:pending:player-a')).toBe(stored)
    expectTakeBackBlocked('Dragon')
    expect(total()).toBe('R2.00')
    expect(balance()).toBe('R100.00')
    await act(async () => { resolve(dragonWin); await Promise.resolve() })
    await advance(1050)
    expect(resetChips()).toHaveProperty('disabled', true)
    fireEvent.click(resetChips())
    expectTakeBackBlocked('Dragon')
    expect(total()).toBe('R2.00')
    await advance(650)
    expect(resetChips()).toHaveProperty('disabled', true)
    expectTakeBackBlocked('Dragon', false)
    expect(total()).toBe('R2.00')
    expect(balance()).toBe('R102.00')
    expect(gateway.createRound).toHaveBeenCalledOnce()
  })

  it('keeps the deal locked while revealing Dragon, Tiger, then the settlement at 1700 ms', async () => {
    const gateway = makeGateway(), changed = vi.fn()
    await start(gateway, 'player-a', changed)
    fireEvent.click(spot('Dragon')); fireEvent.click(spot('Dragon'))
    fireEvent.click(screen.getByRole('button', { name: /Deal cards/ }))
    await flush()
    expect(gateway.createRound).toHaveBeenCalledOnce()
    expect(gateway.createRound.mock.calls[0][0]).toEqual({ dragonStake: 2, tigerStake: 0, tieStake: 0 })
    expect(gateway.createRound.mock.calls[0][1].idempotencyKey).toMatch(/^dragon-tiger-/)
    expect(spot('Dragon')).toHaveProperty('disabled', true)
    expect(screen.queryByLabelText('King of hearts')).toBeNull()
    await advance(299)
    expect(screen.queryByLabelText('King of hearts')).toBeNull()
    await advance(1)
    expect(screen.getByLabelText('King of hearts')).toBeTruthy()
    expect(screen.queryByLabelText('Ace of spades')).toBeNull()
    await advance(750)
    expect(screen.getByLabelText('Ace of spades')).toBeTruthy()
    expect(balance()).toBe('R100.00')
    expect(win()).toBe('R0.00')
    expect(changed).not.toHaveBeenCalled()
    await advance(649)
    expect(balance()).toBe('R100.00')
    await advance(1)
    expect(screen.getByText('DRAGON WINS')).toBeTruthy()
    expect(balance()).toBe('R102.00')
    expect(win()).toBe('R2.00')
    expect(within(screen.getByRole('dialog', { name: 'Round result' })).getByText('R2.00')).toBeTruthy()
    expect(changed).toHaveBeenCalledExactlyOnceWith(102)
    expect(localStorage.getItem('fortuneforge:dragon-tiger:pending:player-a')).toBeNull()
    expect(spot('Dragon')).toHaveProperty('disabled', false)
    fireEvent.keyDown(document, { key: 'Escape' })
    expect(total()).toBe('R0.00')
    expect(balance()).toBe('R102.00')
    fireEvent.click(screen.getByRole('button', { name: /Rebet/ }))
    expect(total()).toBe('R2.00')
    expect(screen.getByRole('button', { name: /Deal cards/ })).toHaveProperty('disabled', false)
    expect(gateway.createRound).toHaveBeenCalledOnce()
  })

  it('delegates each correctly revealed face to the host renderer while keeping unrevealed cards face down', async () => {
    const gateway = makeGateway()
    const renderCardFace = vi.fn((card: DragonTigerCard) => <div aria-label={`Host ${card.rank} of ${card.suit}`}>{card.rank}</div>)
    render(<DragonTigerGame gateway={gateway} playerId="custom-card-renderer" renderCardFace={renderCardFace}/>)
    await flush()
    expect(screen.getAllByLabelText('Face down card')).toHaveLength(2)
    expect(renderCardFace).not.toHaveBeenCalled()
    fireEvent.click(spot('Dragon')); fireEvent.click(spot('Dragon'))
    fireEvent.click(screen.getByRole('button', { name: /Deal cards/ })); await flush()
    expect(screen.getAllByLabelText('Face down card')).toHaveLength(2)
    expect(renderCardFace).not.toHaveBeenCalled()
    await advance(299)
    expect(renderCardFace).not.toHaveBeenCalled()
    await advance(1)
    expect(renderCardFace).toHaveBeenCalledWith(dragonWin.dragonCard)
    expect(renderCardFace).not.toHaveBeenCalledWith(dragonWin.tigerCard)
    expect(within(screen.getByLabelText('Dragon hand')).getByLabelText('Host king of hearts')).toBeTruthy()
    expect(within(screen.getByLabelText('Tiger hand')).getByLabelText('Face down card')).toBeTruthy()
    await advance(749)
    expect(renderCardFace).not.toHaveBeenCalledWith(dragonWin.tigerCard)
    await advance(1)
    expect(renderCardFace).toHaveBeenCalledWith(dragonWin.tigerCard)
    expect(within(screen.getByLabelText('Tiger hand')).getByLabelText('Host ace of spades')).toBeTruthy()
    expect(screen.queryAllByLabelText('Face down card')).toHaveLength(0)
    expect(screen.queryByLabelText('King of hearts')).toBeNull()
    expect(screen.queryByLabelText('Ace of spades')).toBeNull()
    expect(gateway.createRound).toHaveBeenCalledOnce()
  })

  it('shows the combined return and positive net for a tie with all three spots wagered', async () => {
    const tied: DragonTigerRound = {
      ...dragonWin, outcome: 'tie', dragonCard: { rank: 'ace', suit: 'hearts' }, dragonValue: 1,
      balance: 103, totalStake: 2.5, totalReturn: 5.5, profit: 3,
      bets: [
        { side: 'dragon', stake: 1, disposition: 'half-loss', profit: -.5, totalReturn: .5 },
        { side: 'tiger', stake: 1, disposition: 'half-loss', profit: -.5, totalReturn: .5 },
        { side: 'tie', stake: .5, disposition: 'win', profit: 4, totalReturn: 4.5 },
      ],
    }
    await start(makeGateway(tied))
    fireEvent.click(spot('Dragon')); fireEvent.click(spot('Tiger'))
    selectChip(.5); fireEvent.click(spot('Tie'))
    expect(total()).toBe('R2.50')
    fireEvent.click(screen.getByRole('button', { name: /Deal cards/ })); await flush(); await advance(1700)
    expect(screen.getByText('TIE', { exact: true })).toBeTruthy()
    expect(screen.getByText('Tie · half of each Dragon / Tiger bet returned')).toBeTruthy()
    const result = screen.getByRole('dialog', { name: 'Round result' })
    expect(within(result).getByText('YOU WON')).toBeTruthy()
    expect(within(result).getByText('R3.00')).toBeTruthy()
    expect(win()).toBe('R3.00')
    expect(balance()).toBe('R103.00')
  })

  it('shows zero Win and the exact half-return on a losing main bet when the cards tie', async () => {
    const tied: DragonTigerRound = {
      ...dragonWin, outcome: 'tie', dragonCard: { rank: 'ace', suit: 'hearts' }, dragonValue: 1,
      balance: 99.5, totalStake: 1, totalReturn: .5, profit: -.5,
      bets: [
        { side: 'dragon', stake: 1, disposition: 'half-loss', profit: -.5, totalReturn: .5 },
        dragonWin.bets[1], dragonWin.bets[2],
      ],
    }
    await start(makeGateway(tied))
    fireEvent.click(spot('Dragon')); fireEvent.click(screen.getByRole('button', { name: /Deal cards/ }))
    await flush(); await advance(1700)
    expect(win()).toBe('R0.00')
    expect(balance()).toBe('R99.50')
    const result = screen.getByRole('dialog', { name: 'Round result' })
    expect(within(result).getByText('HOUSE WINS')).toBeTruthy()
    expect(within(result).getByText('−R0.50')).toBeTruthy()
  })

  it('finishes immediately when reduced motion is requested', async () => {
    vi.stubGlobal('matchMedia', vi.fn().mockReturnValue({ matches: true }))
    await start()
    fireEvent.click(spot('Dragon')); fireEvent.click(spot('Dragon'))
    fireEvent.click(screen.getByRole('button', { name: /Deal cards/ })); await flush(); await advance(0)
    expect(screen.getByText('DRAGON WINS')).toBeTruthy()
    expect(balance()).toBe('R102.00')
  })

  it('consumes an outside bet-spot click to reset the result without placing a chip, dealing or changing the settled wallet', async () => {
    const gateway = makeGateway()
    gateway.getLatestRound.mockResolvedValue(dragonWin)
    await start(gateway)
    expect(screen.getByText('DRAGON WINS')).toBeTruthy()
    expect(balance()).toBe('R102.00')
    expect(win()).toBe('R2.00')
    const result = screen.getByRole('dialog', { name: 'Round result' })
    fireEvent.click(within(result).getByText('YOU WON'))
    expect(screen.getByRole('dialog', { name: 'Round result' })).toBeTruthy()
    expect(total()).toBe('R2.00')
    fireEvent.click(spot('Tiger'))
    expect(total()).toBe('R0.00')
    expect(balance()).toBe('R102.00')
    expect(win()).toBe('R0.00')
    expect(screen.queryByRole('dialog', { name: 'Round result' })).toBeNull()
    expect(screen.queryByText('DRAGON WINS')).toBeNull()
    expect(screen.queryByLabelText('King of hearts')).toBeNull()
    expect(screen.getAllByLabelText('Face down card')).toHaveLength(2)
    expect(localStorage.getItem('fortuneforge:dragon-tiger:dismissed:player-a')).toBe(dragonWin.roundId)
    expect(gateway.createRound).not.toHaveBeenCalled()
    fireEvent.click(spot('Tiger'))
    expect(total()).toBe('R1.00')
    expect(gateway.createRound).not.toHaveBeenCalled()
  })

  it('persists Escape dismissal per player so a reloaded acknowledged round stays reset', async () => {
    const gateway = makeGateway()
    gateway.getLatestRound.mockResolvedValue(dragonWin)
    const first = await start(gateway)
    expect(screen.getByRole('dialog', { name: 'Round result' })).toBeTruthy()
    fireEvent.keyDown(document, { key: 'Escape' })
    expect(screen.queryByRole('dialog', { name: 'Round result' })).toBeNull()
    expect(total()).toBe('R0.00')
    expect(screen.getAllByLabelText('Face down card')).toHaveLength(2)
    expect(localStorage.getItem('fortuneforge:dragon-tiger:dismissed:player-a')).toBe(dragonWin.roundId)
    first.unmount()
    const second = await start(gateway)
    expect(screen.queryByRole('dialog', { name: 'Round result' })).toBeNull()
    expect(total()).toBe('R0.00')
    expect(balance()).toBe('R102.00')
    expect(win()).toBe('R0.00')
    expect(screen.getAllByLabelText('Face down card')).toHaveLength(2)
    expect(gateway.getLatestRound).toHaveBeenCalledTimes(2)
    expect(gateway.createRound).not.toHaveBeenCalled()
    second.unmount()
    await start(gateway, 'another-player')
    expect(screen.getByRole('dialog', { name: 'Round result' })).toBeTruthy()
    expect(localStorage.getItem('fortuneforge:dragon-tiger:dismissed:another-player')).toBeNull()
  })

  it('shows break-even from the combined net result and keeps the wallet to Bet, Balance and Win', async () => {
    const even: DragonTigerRound = {
      ...dragonWin, balance: 100, totalStake: 2, totalReturn: 2, profit: 0,
      bets: [
        { side: 'dragon', stake: 1, disposition: 'win', profit: 1, totalReturn: 2 },
        { side: 'tiger', stake: 1, disposition: 'loss', profit: -1, totalReturn: 0 },
        dragonWin.bets[2],
      ],
    }
    const gateway = makeGateway(even)
    await start(gateway)
    fireEvent.click(spot('Dragon')); fireEvent.click(spot('Tiger'))
    fireEvent.click(screen.getByRole('button', { name: /Deal cards/ })); await flush(); await advance(1700)
    const result = screen.getByRole('dialog', { name: 'Round result' })
    expect(within(result).getByText('BREAK EVEN')).toBeTruthy()
    expect(within(result).getByText('R0.00')).toBeTruthy()
    expect(balance()).toBe('R100.00')
    expect(win()).toBe('R0.00')
    const wallet = screen.getByTestId('dragon-tiger-balance').closest('footer')!
    expect(wallet.children).toHaveLength(3)
    expect(within(wallet).getByText('BET')).toBeTruthy()
    expect(within(wallet).getByText('BALANCE')).toBeTruthy()
    expect(within(wallet).getByText('WIN')).toBeTruthy()
    expect(wallet.textContent).not.toMatch(/RETURNED|Net/)
    expect(gateway.createRound).toHaveBeenCalledOnce()
  })

  it('refuses additional chips that exceed the wallet or table limit', async () => {
    const gateway = makeGateway()
    gateway.getStatus.mockResolvedValue({ ...status, balance: 2.5 })
    const view = await start(gateway)
    selectChip(2); fireEvent.click(spot('Dragon')); fireEvent.click(spot('Dragon'))
    expect(total()).toBe('R2.00')
    expect(screen.getByRole('alert').textContent).toContain('balance is too low')
    view.unmount()
    const limitedGateway = makeGateway()
    limitedGateway.getStatus.mockResolvedValue({ ...status, maximumStake: 2, maximumTotalStake: 3 })
    await start(limitedGateway)
    selectChip(2); fireEvent.click(spot('Dragon')); fireEvent.click(spot('Tiger'))
    expect(total()).toBe('R2.00')
    expect(screen.getByRole('alert').textContent).toContain('Table limit')
    expect(limitedGateway.createRound).not.toHaveBeenCalled()
  })

  it('triggers selection, deal and result sounds while honoring the current mute setting', async () => {
    await start()
    selectChip(2); fireEvent.click(spot('Dragon'))
    expect(playDragonTigerAudio).toHaveBeenCalledWith('chip', false)
    expect(playDragonTigerAudio).toHaveBeenCalledWith('click', false)
    fireEvent.click(screen.getByRole('button', { name: 'Mute sound' }))
    fireEvent.click(screen.getByRole('button', { name: /Deal cards/ })); await flush(); await advance(1700)
    expect(playDragonTigerAudio).toHaveBeenCalledWith('click', true)
    expect(playDragonTigerAudio).toHaveBeenCalledWith('deal', true)
    expect(playDragonTigerAudio).toHaveBeenCalledWith('win', true)
  })
})

describe('DragonTigerGame pending deal recovery', () => {
  it.each([401, 403, 400, 503])('preserves an uncertain pending deal after an HTTP %s response and restores the same key', async httpStatus => {
    const gateway = makeGateway()
    gateway.createRound.mockRejectedValueOnce(new DragonTigerGatewayError('Session must be restored.', httpStatus === 503 ? 'dragon-tiger-disabled' : 'authentication-required', httpStatus))
    await start(gateway)
    fireEvent.click(spot('Dragon')); fireEvent.click(spot('Dragon'))
    fireEvent.click(screen.getByRole('button', { name: /Deal cards/ })); await flush()
    const original = gateway.createRound.mock.calls[0]
    expect(JSON.parse(localStorage.getItem('fortuneforge:dragon-tiger:pending:player-a')!).key).toBe(original[1].idempotencyKey)
    expect(spot('Tiger')).toHaveProperty('disabled', true)
    expectTakeBackBlocked('Dragon')
    const stored = localStorage.getItem('fortuneforge:dragon-tiger:pending:player-a')
    expect(resetChips()).toHaveProperty('disabled', true)
    fireEvent.click(resetChips())
    expect(localStorage.getItem('fortuneforge:dragon-tiger:pending:player-a')).toBe(stored)
    expect(gateway.createRound).toHaveBeenCalledOnce()
    expect(total()).toBe('R2.00')
    fireEvent.click(screen.getByRole('button', { name: /Restore deal/ })); await flush(); await advance(1700)
    expect(gateway.createRound.mock.calls[1][1].idempotencyKey).toBe(original[1].idempotencyKey)
    expect(balance()).toBe('R102.00')
  })

  it.each([false, true])('ignores a previous account’s delayed response after switching players (rejected: %s)', async rejectOld => {
    let resolve!: (round: DragonTigerRound) => void
    let reject!: (error: unknown) => void
    const delayed = new Promise<DragonTigerRound>((resolvePromise, rejectPromise) => { resolve = resolvePromise; reject = rejectPromise })
    const oldGateway = makeGateway(), newGateway = makeGateway(), changed = vi.fn()
    oldGateway.createRound.mockReturnValueOnce(delayed)
    newGateway.getStatus.mockResolvedValue({ ...status, balance: 700 })
    const view = await start(oldGateway, 'old-player', changed)
    fireEvent.click(spot('Dragon')); fireEvent.click(spot('Dragon'))
    fireEvent.click(screen.getByRole('button', { name: /Deal cards/ })); await flush()
    const oldPending = localStorage.getItem('fortuneforge:dragon-tiger:pending:old-player')
    expect(oldPending).not.toBeNull()
    view.rerender(<DragonTigerGame gateway={newGateway} playerId="new-player" onBalanceChange={changed}/>); await flush()
    await act(async () => { if (rejectOld) reject(new TypeError('Old account response failed')); else resolve(dragonWin); await Promise.resolve() })
    await advance(1700)
    expect(balance()).toBe('R700.00')
    expect(total()).toBe('R0.00')
    expect(win()).toBe('R0.00')
    expect(changed).not.toHaveBeenCalled()
    expect(screen.queryByRole('alert')).toBeNull()
    expect(screen.queryByLabelText('King of hearts')).toBeNull()
    expect(localStorage.getItem('fortuneforge:dragon-tiger:pending:old-player')).toBe(oldPending)
    expect(localStorage.getItem('fortuneforge:dragon-tiger:pending:new-player')).toBeNull()
    expect(spot('Dragon')).toHaveProperty('disabled', false)
  })

  it('ignores a previous account’s delayed balance refresh after a rejected wager', async () => {
    let resolveStatus!: (value: DragonTigerStatus) => void
    const delayedStatus = new Promise<DragonTigerStatus>(resolve => { resolveStatus = resolve })
    const oldGateway = makeGateway(), newGateway = makeGateway()
    oldGateway.getStatus.mockResolvedValueOnce(status).mockReturnValueOnce(delayedStatus)
    oldGateway.createRound.mockRejectedValueOnce(new DragonTigerGatewayError('Invalid wager.', 'dragon-tiger-invalid-request', 400))
    newGateway.getStatus.mockResolvedValue({ ...status, balance: 700 })
    const view = await start(oldGateway, 'old-player')
    fireEvent.click(spot('Dragon')); fireEvent.click(screen.getByRole('button', { name: /Deal cards/ })); await flush()
    expect(oldGateway.getStatus).toHaveBeenCalledTimes(2)
    view.rerender(<DragonTigerGame gateway={newGateway} playerId="new-player"/>); await flush()
    await act(async () => { resolveStatus({ ...status, balance: 50 }); await Promise.resolve() })
    expect(balance()).toBe('R700.00')
    expect(spot('Dragon')).toHaveProperty('disabled', false)
  })

  it('persists a lost-response wager, blocks new betting, and replays the same key after reload', async () => {
    const gateway = makeGateway()
    gateway.createRound.mockRejectedValueOnce(new TypeError('Connection lost after server settlement'))
    const first = await start(gateway)
    fireEvent.click(spot('Dragon')); fireEvent.click(spot('Dragon'))
    fireEvent.click(screen.getByRole('button', { name: /Deal cards/ })); await flush()
    expect(screen.getByRole('button', { name: /Restore deal/ })).toBeTruthy()
    expect(spot('Tiger')).toHaveProperty('disabled', true)
    const persisted = JSON.parse(localStorage.getItem('fortuneforge:dragon-tiger:pending:player-a')!)
    const original = gateway.createRound.mock.calls[0]
    expect(persisted.key).toBe(original[1].idempotencyKey)
    expect(persisted.stakes).toEqual(original[0])
    first.unmount()
    await start(gateway)
    expect(gateway.createRound).toHaveBeenCalledTimes(2)
    expect(gateway.createRound.mock.calls[1][0]).toEqual(original[0])
    expect(gateway.createRound.mock.calls[1][1].idempotencyKey).toBe(original[1].idempotencyKey)
    expect(total()).toBe('R2.00')
    await advance(1700)
    expect(balance()).toBe('R102.00')
    expect(localStorage.getItem('fortuneforge:dragon-tiger:pending:player-a')).toBeNull()
    const history = JSON.parse(localStorage.getItem('fortuneforge:dragon-tiger:history:player-a')!)
    expect(history).toEqual([{ id: 'client-round-1', outcome: 'dragon' }])
    expect(screen.queryByRole('complementary', { name: 'Recent results' })).toBeNull()
    expect(screen.queryByText('Recent results')).toBeNull()
  })

  it('restores in place with the original key after a failed request', async () => {
    const gateway = makeGateway()
    gateway.createRound.mockRejectedValueOnce(new TypeError('Network unavailable'))
    await start(gateway)
    fireEvent.click(spot('Dragon')); fireEvent.click(spot('Dragon'))
    fireEvent.click(screen.getByRole('button', { name: /Deal cards/ })); await flush()
    fireEvent.click(screen.getByRole('button', { name: /Restore deal/ })); await flush()
    expect(gateway.createRound.mock.calls[1][1].idempotencyKey).toBe(gateway.createRound.mock.calls[0][1].idempotencyKey)
    await advance(1700)
    expect(win()).toBe('R2.00')
  })

  it('keeps another account’s pending deal and history isolated', async () => {
    const otherPending = {
      key: 'dragon-tiger-00000000-0000-4000-8000-000000000001',
      stakes: { dragonStake: 2, tigerStake: 0, tieStake: 0 },
      placements: [{ side: 'dragon', value: 2 }],
    }
    localStorage.setItem('fortuneforge:dragon-tiger:pending:other-player', JSON.stringify(otherPending))
    localStorage.setItem('fortuneforge:dragon-tiger:history:other-player', JSON.stringify([{ id: 'secret-round', outcome: 'tie' }]))
    const gateway = makeGateway()
    const view = await start(gateway)
    expect(gateway.createRound).not.toHaveBeenCalled()
    expect(total()).toBe('R0.00')
    expect(screen.queryByRole('complementary', { name: 'Recent results' })).toBeNull()
    expect(screen.queryByText('Your results will appear here')).toBeNull()
    expect(localStorage.getItem('fortuneforge:dragon-tiger:history:other-player')).toBe(JSON.stringify([{ id: 'secret-round', outcome: 'tie' }]))
    expect(localStorage.getItem('fortuneforge:dragon-tiger:pending:other-player')).toBe(JSON.stringify(otherPending))
    view.rerender(<DragonTigerGame gateway={gateway} playerId="other-player"/>); await flush()
    expect(gateway.createRound).toHaveBeenCalledExactlyOnceWith(otherPending.stakes, expect.objectContaining({ idempotencyKey: otherPending.key }))
    expect(total()).toBe('R2.00')
  })

  it('discards a confirmed server rejection so the player can correct the wager', async () => {
    const gateway = makeGateway()
    gateway.createRound.mockRejectedValueOnce(new DragonTigerGatewayError('Insufficient balance.', 'insufficient-slot-credits', 400))
    await start(gateway)
    fireEvent.click(spot('Dragon')); fireEvent.click(spot('Dragon'))
    fireEvent.click(screen.getByRole('button', { name: /Deal cards/ })); await flush()
    expect(screen.getByRole('alert').textContent).toContain('Insufficient balance.')
    expect(localStorage.getItem('fortuneforge:dragon-tiger:pending:player-a')).toBeNull()
    expect(spot('Dragon')).toHaveProperty('disabled', false)
    expect(screen.queryByRole('button', { name: /Restore deal/ })).toBeNull()
  })

  it('ignores a stored request whose placements disagree with its stakes', async () => {
    localStorage.setItem('fortuneforge:dragon-tiger:pending:player-a', JSON.stringify({
      key: 'dragon-tiger-00000000-0000-4000-8000-000000000001',
      stakes: { dragonStake: 50, tigerStake: 0, tieStake: 0 },
      placements: [{ side: 'dragon', value: 1 }],
    }))
    const gateway = makeGateway()
    await start(gateway)
    expect(gateway.createRound).not.toHaveBeenCalled()
    expect(total()).toBe('R0.00')
    expect(spot('Dragon')).toHaveProperty('disabled', false)
  })

  it('can restore in memory when optional browser storage is unavailable', async () => {
    const gateway = makeGateway()
    gateway.createRound.mockRejectedValueOnce(new TypeError('Network unavailable'))
    vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => { throw new DOMException('Storage blocked', 'SecurityError') })
    await start(gateway)
    fireEvent.click(spot('Dragon')); fireEvent.click(spot('Dragon'))
    fireEvent.click(screen.getByRole('button', { name: /Deal cards/ })); await flush()
    fireEvent.click(screen.getByRole('button', { name: /Restore deal/ })); await flush(); await advance(1700)
    expect(gateway.createRound.mock.calls[1][1].idempotencyKey).toBe(gateway.createRound.mock.calls[0][1].idempotencyKey)
    expect(balance()).toBe('R102.00')
  })
})
