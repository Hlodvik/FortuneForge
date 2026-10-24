// @vitest-environment jsdom
import { act, cleanup, fireEvent, render, screen, within } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { HoldemActionBar } from './HoldemActionBar'
import { testTable } from './testFixtures'
import type { HoldemTable } from './contracts'

afterEach(() => { cleanup(); vi.useRealTimers() })
const setup = (table: HoldemTable = testTable, busy = false) => {
  const onAction = vi.fn(), onLeave = vi.fn()
  const result = render(<HoldemActionBar table={table} busy={busy} onAction={onAction} onLeave={onLeave} />)
  return { ...result, onAction, onLeave }
}

describe('HoldemActionBar authoritative actions', () => {
  it('keeps the three round actions separate from the compact raise adjustment controls', () => {
    const { container, onAction } = setup()
    const rounds = container.querySelector('.ff-holdem-round-actions') as HTMLElement
    const adjustment = container.querySelector('.ff-holdem-raise-control') as HTMLElement

    expect(rounds.parentElement?.classList.contains('ff-holdem-actions')).toBe(true)
    expect(adjustment.parentElement).toBe(rounds.parentElement)
    expect(within(rounds).getAllByRole('button').map(button => button.textContent)).toEqual(['Fold', 'Call R1.00', 'Raise R4.00'])
    expect(rounds.querySelectorAll(':scope > button')).toHaveLength(3)
    expect(within(adjustment).queryByRole('button', { name: /^Raise R/ })).toBeNull()
    expect(within(adjustment).getByLabelText('Selected raise amount').tagName).toBe('OUTPUT')
    expect(within(adjustment).getByLabelText('Selected raise amount').textContent).toBe('R4.00')
    fireEvent.click(within(adjustment).getByRole('button', { name: 'Increase raise' }))
    expect(within(adjustment).getByLabelText('Selected raise amount').textContent).toBe('R5.00')
    fireEvent.click(within(rounds).getByRole('button', { name: 'Raise R5.00' }))
    expect(onAction).toHaveBeenCalledWith('raise', 500)
  })

  it('formats cent-based call/raise amounts as rand and sends the unchanged authoritative action', () => {
    const { onAction } = setup()
    fireEvent.click(screen.getByRole('button', { name: 'Call R1.00' }))
    expect(onAction).toHaveBeenCalledWith('call', undefined)
    fireEvent.click(screen.getByRole('button', { name: 'Raise R4.00' }))
    expect(onAction).toHaveBeenLastCalledWith('raise', 400)
    expect(screen.queryByRole('button', { name: 'Check' })).toBeNull()
  })

  it.each([
    ['another player acts', { ...testTable, activeSeat: 1 }],
    ['viewer is folded', { ...testTable, seats: testTable.seats.map(seat => seat.isCurrentPlayer ? { ...seat, status: 'folded' } : seat) }],
    ['hand ended', { ...testTable, status: 'completed' }],
    ['viewer is missing', { ...testTable, seats: testTable.seats.filter(seat => !seat.isCurrentPlayer) }],
  ])('does not act merely because stale legalActions exist when %s', (_label, table) => {
    const { onAction } = setup(table)
    for (const name of ['Fold', 'Call R1.00', 'Raise R4.00', '½ pot', 'Pot', 'All in']) {
      const button = screen.queryByRole('button', { name }) as HTMLButtonElement | null
      if (!button) continue
      expect(button.disabled).toBe(true)
      fireEvent.click(button)
    }
    expect((screen.getByRole('slider', { name: 'Raise amount' }) as HTMLInputElement).disabled).toBe(true)
    expect(onAction).not.toHaveBeenCalled()
  })

  it('allows check only when the server lists check and the viewer is active', () => {
    const { onAction } = setup({ ...testTable, legalActions: ['check', 'fold'] })
    expect(screen.queryByRole('button', { name: /^Call/ })).toBeNull()
    expect(screen.queryByRole('slider')).toBeNull()
    fireEvent.click(screen.getByRole('button', { name: 'Check' }))
    expect(onAction).toHaveBeenCalledWith('check', undefined)
  })

  it('disables every command, preset and leave while a mutation is busy', () => {
    const { onAction, onLeave } = setup(testTable, true)
    for (const button of screen.getAllByRole('button')) expect((button as HTMLButtonElement).disabled).toBe(true)
    fireEvent.click(screen.getByRole('button', { name: 'Leave table' }))
    fireEvent.click(screen.getByRole('button', { name: 'Call R1.00' }))
    expect(onAction).not.toHaveBeenCalled()
    expect(onLeave).not.toHaveBeenCalled()
  })

  it('limits the displayed call to the viewer’s remaining stack for an all-in call', () => {
    setup({ ...testTable, seats: testTable.seats.map(seat => seat.isCurrentPlayer ? { ...seat, stack: 50 } : seat) })
    expect(screen.getByRole('button', { name: 'Call R0.50' })).toBeTruthy()
  })
})

describe('HoldemActionBar raise targets', () => {
  it('steps by one big blind, clamps to server bounds, and resets on a new street', () => {
    const { rerender, onAction, onLeave } = setup()
    const decrease = screen.getByRole('button', { name: 'Decrease raise' }) as HTMLButtonElement
    expect(decrease.disabled).toBe(true)
    fireEvent.click(screen.getByRole('button', { name: 'Increase raise' }))
    expect(screen.getByRole('button', { name: 'Raise R5.00' })).toBeTruthy()
    fireEvent.change(screen.getByRole('slider'), { target: { value: '2500' } })
    expect((screen.getByRole('button', { name: 'Increase raise' }) as HTMLButtonElement).disabled).toBe(true)
    expect(screen.getByRole('button', { name: 'Raise R25.00' })).toBeTruthy()
    rerender(<HoldemActionBar table={{ ...testTable, street: 'turn', minimumRaiseTo: 600 }} busy={false} onAction={onAction} onLeave={onLeave} />)
    expect(screen.getByRole('button', { name: 'Raise R6.00' })).toBeTruthy()
  })

  it('uses table-cent half-pot, pot and all-in targets without premature action', () => {
    const { onAction } = setup()
    fireEvent.click(screen.getByRole('button', { name: '½ pot' }))
    expect(screen.getByRole('button', { name: 'Raise R7.00' })).toBeTruthy()
    fireEvent.click(screen.getByRole('button', { name: 'Pot' }))
    expect(screen.getByRole('button', { name: 'Raise R12.00' })).toBeTruthy()
    fireEvent.click(screen.getByRole('button', { name: 'All in' }))
    expect(screen.getByRole('button', { name: 'Raise R25.00' })).toBeTruthy()
    expect(onAction).not.toHaveBeenCalled()
  })

  it('clamps presets to the minimum and supports an exact off-step all-in maximum', () => {
    setup({ ...testTable, pot: 0, maximumRaiseTo: 2533 })
    fireEvent.click(screen.getByRole('button', { name: '½ pot' }))
    expect(screen.getByRole('button', { name: 'Raise R4.00' })).toBeTruthy()
    fireEvent.click(screen.getByRole('button', { name: 'All in' }))
    expect((screen.getByRole('slider') as HTMLInputElement).value).toBe('2533')
    expect(screen.getByRole('button', { name: 'Raise R25.33' })).toBeTruthy()
  })

  it('preserves the short-all-in exception below the normal minimum raise', () => {
    const { onAction } = setup({ ...testTable, minimumRaiseTo: 600, maximumRaiseTo: 350, shortAllInRaiseTo: 350 })
    const slider = screen.getByRole('slider') as HTMLInputElement
    expect(slider.min).toBe('350')
    expect(slider.max).toBe('350')
    expect(slider.disabled).toBe(true)
    fireEvent.click(screen.getByRole('button', { name: 'Raise R3.50' }))
    expect(onAction).toHaveBeenCalledWith('raise', 350)
  })

  it('counts down even when only remaining milliseconds, not an absolute deadline, are supplied', () => {
    vi.useFakeTimers()
    setup()
    expect(screen.getByRole('timer').textContent).toContain('30')
    act(() => vi.advanceTimersByTime(2500))
    expect(screen.getByRole('timer').textContent).toContain('28')
  })
})

describe('HoldemActionBar leave confirmation', () => {
  it('requires explicit confirmation during a hand and lets the player safely stay', () => {
    const { onLeave } = setup()
    fireEvent.click(screen.getByRole('button', { name: 'Leave table' }))
    const dialog = screen.getByRole('alertdialog', { name: 'Leave the table?' })
    expect(onLeave).not.toHaveBeenCalled()
    expect(document.activeElement).toBe(within(dialog).getByRole('button', { name: 'Stay at table' }))
    expect((screen.getByRole('button', { name: 'Call R1.00' }) as HTMLButtonElement).disabled).toBe(true)
    fireEvent.click(within(dialog).getByRole('button', { name: 'Stay at table' }))
    expect(screen.queryByRole('alertdialog')).toBeNull()
    expect(onLeave).not.toHaveBeenCalled()
    fireEvent.click(screen.getByRole('button', { name: 'Leave table' }))
    fireEvent.click(within(screen.getByRole('alertdialog')).getByRole('button', { name: 'Leave table' }))
    expect(onLeave).toHaveBeenCalledOnce()
  })

  it('cancels with Escape and restores focus without sending a leave', () => {
    const { onLeave } = setup()
    const leave = screen.getByRole('button', { name: 'Leave table' })
    fireEvent.click(leave)
    fireEvent.keyDown(screen.getByRole('alertdialog'), { key: 'Escape' })
    expect(screen.queryByRole('alertdialog')).toBeNull()
    expect(document.activeElement).toBe(leave)
    expect(onLeave).not.toHaveBeenCalled()
  })

  it('keeps keyboard focus within confirmation before restoring the leave button', () => {
    setup()
    const leave = screen.getByRole('button', { name: 'Leave table' })
    fireEvent.click(leave)
    const dialog = screen.getByRole('alertdialog')
    const stay = within(dialog).getByRole('button', { name: 'Stay at table' })
    const confirm = within(dialog).getByRole('button', { name: 'Leave table' })
    fireEvent.keyDown(dialog, { key: 'Tab', shiftKey: true })
    expect(document.activeElement).toBe(confirm)
    fireEvent.keyDown(dialog, { key: 'Tab' })
    expect(document.activeElement).toBe(stay)
    fireEvent.keyDown(dialog, { key: 'Escape' })
    expect(document.activeElement).toBe(leave)
  })

  it('allows leaving an already completed hand without an unnecessary confirmation', () => {
    const { onLeave } = setup({ ...testTable, status: 'completed', legalActions: [] })
    fireEvent.click(screen.getByRole('button', { name: 'Leave table' }))
    expect(screen.queryByRole('alertdialog')).toBeNull()
    expect(onLeave).toHaveBeenCalledOnce()
  })
})
