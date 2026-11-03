// @vitest-environment jsdom
import { act, type ComponentProps } from 'react'
import { createRoot, type Root } from 'react-dom/client'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import type { GridPosition, PaylinePayout } from '../../../features/slots/types/slots'
import { MidnightWinningPaylines } from './MidnightWinningPaylines'
import { getMidnightPaidPaths } from './midnightPaidPaths'

const line = (id: number, positions: GridPosition[], amountPoints = 10): PaylinePayout => ({
  paylineId: id, amountPoints, matches: [{ amountPoints, multiplier: 1,
    match: { paylineId: id, symbolId: '2', matchLength: positions.length, positions, wildPositions: [] } }],
})
const first = line(1, [{ reel: 0, row: 0 }, { reel: 1, row: 2 }, { reel: 2, row: 3 }])
const second = line(2, [{ reel: 2, row: 0 }, { reel: 3, row: 1 }, { reel: 4, row: 2 }])
let host: HTMLDivElement
let root: Root
let resize: () => void

function Harness({
  rows = 4, paylines = [first], visible = true, resultKey = 'first', reducedMotion = false,
  palette, formatWinAmount, onActivePathChange,
}: {
  rows?: number; paylines?: PaylinePayout[]; visible?: boolean; resultKey?: string; reducedMotion?: boolean
  palette?: ComponentProps<typeof MidnightWinningPaylines>['palette']
  formatWinAmount?: (amountPoints: number) => string
  onActivePathChange?: ComponentProps<typeof MidnightWinningPaylines>['onActivePathChange']
}) {
  return <div className="slots-page__cabinet-spotlight" data-test-rows={rows}>
    <div className="slot-machine__reels">{Array.from({ length: 5 }, (_, reel) =>
      Array.from({ length: rows }, (_, row) => <span key={`${reel}:${row}`} className="slot-symbol"
        data-reel-index={reel} data-row-index={row} />))}</div>
    <MidnightWinningPaylines paylines={paylines} visible={visible} resultKey={resultKey}
      reducedMotion={reducedMotion} palette={palette} formatWinAmount={formatWinAmount} onActivePathChange={onActivePathChange} />
  </div>
}
const activeLine = () => host.querySelector('g[data-payline-id]')?.getAttribute('data-payline-id')
const points = () => host.querySelector('g[data-payline-id]')?.getAttribute('data-points')

beforeEach(() => {
  vi.useFakeTimers()
  vi.stubGlobal('IS_REACT_ACT_ENVIRONMENT', true)
  vi.stubGlobal('ResizeObserver', class {
    constructor(callback: () => void) { resize = callback }
    observe() { /* measured by the explicit test callback */ }
    disconnect() { /* no observer survives unmount */ }
  })
  vi.spyOn(Element.prototype, 'getBoundingClientRect').mockImplementation(function (this: Element) {
    if (this.classList.contains('midnight-winning-paylines')) return new DOMRect(100, 50, 500, 300)
    if (this.classList.contains('slot-symbol')) {
      const rows = Number(this.closest('[data-test-rows]')?.getAttribute('data-test-rows'))
      const reel = Number(this.getAttribute('data-reel-index'))
      const row = Number(this.getAttribute('data-row-index'))
      return new DOMRect(114 + reel * 94, 61 + row * (rows === 4 ? 62 : 42), 76, rows === 4 ? 50 : 32)
    }
    return new DOMRect(100, 50, 500, 300)
  })
  host = document.createElement('div')
  document.body.append(host)
  root = createRoot(host)
})
afterEach(() => {
  act(() => root.unmount())
  host.remove()
  vi.useRealTimers()
  vi.restoreAllMocks()
  vi.unstubAllGlobals()
})

describe('Midnight paid line pulse', () => {
  it('keeps separate authoritative matches, drops unpaid/invalid paths and deduplicates identical geometry', () => {
    const separate = { ...first, matches: [...first.matches, ...second.matches] }
    const paths = getMidnightPaidPaths([separate, line(3, first.matches[0].match.positions),
      line(4, second.matches[0].match.positions, 0), line(5, [{ reel: -1, row: 0 }, { reel: 1, row: 0 }])])
    expect(paths.map(path => path.positions)).toEqual([first.matches[0].match.positions, second.matches[0].match.positions])
    expect(paths.map(path => path.amountPoints)).toEqual([20, 10])
    expect(separate.matches).toHaveLength(2)
  })

  it.each([4, 6])('measures actual symbol centers in a %i-row grid rather than assuming cell padding', rows => {
    const paid = line(7, [{ reel: 0, row: 0 }, { reel: 1, row: 2 }, { reel: 2, row: rows - 1 }])
    act(() => root.render(<Harness rows={rows} paylines={[paid]} />))
    expect(points()).toBe(rows === 4 ? '52,36 146,160 240,222' : '52,27 146,111 240,237')
    expect(host.querySelector('svg')?.getAttribute('aria-hidden')).toBe('true')
    expect(host.querySelectorAll('g[data-payline-id]')).toHaveLength(1)
  })

  it('cycles one winning match at a time and resets to the first line for a fresh result', () => {
    const paylines = [first, second]
    const onActivePathChange = vi.fn()
    act(() => root.render(<Harness palette="neon" paylines={paylines} onActivePathChange={onActivePathChange} />))
    expect(activeLine()).toBe('1')
    expect(onActivePathChange).toHaveBeenLastCalledWith({ resultKey: 'first', positions: first.matches[0].match.positions })
    act(() => vi.advanceTimersByTime(2200))
    expect(activeLine()).toBe('2')
    expect(onActivePathChange).toHaveBeenLastCalledWith({ resultKey: 'first', positions: second.matches[0].match.positions })
    expect(host.querySelectorAll('g[data-payline-id]')).toHaveLength(1)
    act(() => root.render(<Harness palette="neon" paylines={paylines} resultKey="next" onActivePathChange={onActivePathChange} />))
    expect(activeLine()).toBe('1')
    expect(onActivePathChange).toHaveBeenLastCalledWith({ resultKey: 'next', positions: first.matches[0].match.positions })
    act(() => root.render(<Harness palette="neon" paylines={paylines} visible={false} onActivePathChange={onActivePathChange} />))
    expect(onActivePathChange).toHaveBeenLastCalledWith(null)
  })

  it('clears immediately on a new spin or a result without paid lines', () => {
    act(() => root.render(<Harness paylines={[first, second]} />))
    act(() => root.render(<Harness visible={false} paylines={[first, second]} />))
    expect(host.querySelector('svg')).toBeNull()
    act(() => root.render(<Harness paylines={[]} resultKey="loss" />))
    expect(host.querySelector('svg')).toBeNull()
  })

  it('shows the active match award instead of repeating the total spin win', () => {
    const other = line(2, second.matches[0].match.positions, 24)
    const formatWinAmount = (points: number) => `+R${(points * .25).toFixed(2)}`
    act(() => root.render(<Harness paylines={[first, other]} formatWinAmount={formatWinAmount} />))
    expect(host.querySelector('text')?.textContent).toBe('+R2.50')
    act(() => vi.advanceTimersByTime(2200))
    expect(host.querySelector('text')?.textContent).toBe('+R6.00')
    act(() => root.render(<Harness paylines={[first, other]} visible={false} formatWinAmount={formatWinAmount} />))
    expect(host.querySelector('text')).toBeNull()
  })

  it('centers each Rainbow award on the matching path and keeps the whole path visible', () => {
    const paylines = [
      line(1, [0, 1, 2].map(reel => ({ reel, row: 0 })), 10),
      line(2, [2, 3, 4].map(reel => ({ reel, row: 2 })), 24),
    ]
    act(() => root.render(<Harness palette="rainbow" paylines={paylines} formatWinAmount={points => `+R${(points * .25).toFixed(2)}`} />))

    const label = () => host.querySelector('text')!
    expect(label().textContent).toBe('+R2.50')
    expect(label().getAttribute('dominant-baseline')).toBe('central')
    expect(label().getAttribute('x')).toBe('146')
    expect(label().getAttribute('y')).toBe('36')
    expect(host.querySelector('.rainbow-realm__steady-payline')?.getAttribute('points')).toBe(points())

    act(() => vi.advanceTimersByTime(2200))

    expect(label().textContent).toBe('+R6.00')
    expect(label().getAttribute('x')).toBe('334')
    expect(label().getAttribute('y')).toBe('160')
    expect(host.querySelector('.rainbow-realm__steady-payline')?.getAttribute('points')).toBe(points())
  })

  it('keeps a single static line for reduced motion', () => {
    act(() => root.render(<Harness paylines={[first, second]} reducedMotion />))
    act(() => vi.advanceTimersByTime(22_000))
    expect(activeLine()).toBe('1')
    expect(host.querySelector('svg')?.getAttribute('data-reduced-motion')).toBe('true')
  })

  it('never draws a partial guessed path and remeasures when an enhanced grid appears', () => {
    const paid = line(8, [{ reel: 0, row: 0 }, { reel: 1, row: 2 }, { reel: 2, row: 5 }])
    act(() => root.render(<Harness paylines={[paid]} />))
    expect(host.querySelector('g')).toBeNull()
    act(() => root.render(<Harness rows={6} paylines={[paid]} />))
    act(() => resize())
    expect(points()).toBe('52,27 146,111 240,237')
  })
})
