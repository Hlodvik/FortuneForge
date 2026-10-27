// @vitest-environment jsdom
import { useState } from 'react'
import { cleanup, fireEvent, render, screen } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { CrapsBetControls, type CrapsBetControlsProps, type CrapsChipDenomination } from './CrapsBetControls'

const denominations: readonly CrapsChipDenomination[] = [.25, .5, 1, 2, 5, 10, 25, 50]
  .map(value => ({ value, picture: `/chip-${value}.png`, artScale: value === .5 ? 1.1 : 1 }))

afterEach(cleanup)

function props(overrides: Partial<CrapsBetControlsProps> = {}): CrapsBetControlsProps {
  return {
    denominations, selectedChip: 2, disabled: false, canUndo: true, canClear: true,
    onSelectChip: vi.fn(), onUndo: vi.fn(), onClear: vi.fn(), ...overrides,
  }
}
function control(name: string) { return screen.getByRole('button', { name }) as HTMLButtonElement }
function picker() { return screen.getByRole('group', { name: 'Select chip value' }) }
function open(selected = 'R2') { fireEvent.click(control(`Chip options, selected ${selected}`)) }

describe('compact Craps bet controls', () => {
  it('shows just the selected asset until the player opens the denominations', () => {
    render(<CrapsBetControls {...props({ selectedChip: .5 })}/> )
    expect(picker().querySelectorAll('img')).toHaveLength(1)
    const trigger = control('Chip options, selected R0.5')
    expect(trigger.getAttribute('aria-expanded')).toBe('false')
    expect(trigger.querySelector('img')?.getAttribute('src')).toBe('/chip-0.5.png')
    expect(trigger.querySelector('img')?.getAttribute('draggable')).toBe('false')
    expect(trigger.querySelector('img')?.style.transform).toBe('scale(1.1)')
    expect(screen.queryByRole('group', { name: 'Chip denominations' })).toBeNull()
    expect(control('Increase chip value').disabled).toBe(false)
    expect(control('Decrease chip value').disabled).toBe(false)
  })

  it('opens all eight options, connects the trigger to them and focuses the current value', () => {
    render(<CrapsBetControls {...props()}/> )
    open()
    const choices = screen.getByRole('group', { name: 'Chip denominations' })
    const trigger = control('Chip options, selected R2')
    expect(trigger.getAttribute('aria-expanded')).toBe('true')
    expect(trigger.getAttribute('aria-controls')).toBe(choices.id)
    expect(choices.querySelectorAll('button')).toHaveLength(8)
    expect(choices.querySelectorAll('img')).toHaveLength(8)
    expect(control('Select R2 chip').getAttribute('aria-pressed')).toBe('true')
    expect(control('Select R0.25 chip').getAttribute('aria-pressed')).toBe('false')
    expect(document.activeElement).toBe(control('Select R2 chip'))
  })

  it('selects the exact value once, closes the tray and returns focus to the selected chip', () => {
    const select = vi.fn()
    const initial = props({ onSelectChip: select })
    const view = render(<CrapsBetControls {...initial}/> )
    open()
    fireEvent.click(control('Select R25 chip'))
    expect(select).toHaveBeenCalledExactlyOnceWith(25)
    expect(screen.queryByRole('group', { name: 'Chip denominations' })).toBeNull()
    expect(document.activeElement).toBe(control('Chip options, selected R2'))
    view.rerender(<CrapsBetControls {...initial} selectedChip={25}/>)
    expect(control('Chip options, selected R25').querySelector('img')?.getAttribute('src')).toBe('/chip-25.png')
  })

  it('lets the selected chip toggle its tray closed without changing the denomination', () => {
    const input = props()
    render(<CrapsBetControls {...input}/> )
    open()
    fireEvent.click(control('Chip options, selected R2'))
    expect(screen.queryByRole('group', { name: 'Chip denominations' })).toBeNull()
    expect(input.onSelectChip).not.toHaveBeenCalled()
  })

  it('steps through all eight denominations in order and stops at each endpoint', () => {
    const select = vi.fn()
    function ControlledPicker() {
      const [selectedChip, setSelectedChip] = useState(.25)
      return <CrapsBetControls {...props({ selectedChip, onSelectChip: value => { select(value); setSelectedChip(value) } })}/>
    }
    render(<ControlledPicker/>)
    expect(control('Decrease chip value').disabled).toBe(true)
    fireEvent.click(control('Decrease chip value'))
    expect(select).not.toHaveBeenCalled()
    for (const value of [.5, 1, 2, 5, 10, 25, 50]) {
      fireEvent.click(control('Increase chip value'))
      expect(select).toHaveBeenLastCalledWith(value)
      expect(control(`Chip options, selected R${value}`).getAttribute('aria-expanded')).toBe('false')
    }
    expect(control('Increase chip value').disabled).toBe(true)
    fireEvent.click(control('Increase chip value'))
    expect(select).toHaveBeenCalledTimes(7)
    for (const value of [25, 10, 5, 2, 1, .5, .25]) {
      fireEvent.click(control('Decrease chip value'))
      expect(select).toHaveBeenLastCalledWith(value)
    }
    expect(control('Decrease chip value').disabled).toBe(true)
    expect(control('Increase chip value').disabled).toBe(false)
    expect(select.mock.calls.map(call => call[0])).toEqual([.5, 1, 2, 5, 10, 25, 50, 25, 10, 5, 2, 1, .5, .25])
  })

  it('closes on Escape without selecting and restores focus to the trigger', () => {
    const input = props()
    render(<CrapsBetControls {...input}/> )
    open()
    const escape = new KeyboardEvent('keydown', { key: 'Escape', bubbles: true, cancelable: true })
    fireEvent(document, escape)
    expect(escape.defaultPrevented).toBe(true)
    expect(screen.queryByRole('group', { name: 'Chip denominations' })).toBeNull()
    expect(document.activeElement).toBe(control('Chip options, selected R2'))
    expect(input.onSelectChip).not.toHaveBeenCalled()
  })

  it('ignores pointer presses inside the picker but closes outside without stealing focus', () => {
    const input = props()
    render(<><CrapsBetControls {...input}/><button type="button">Outside the picker</button></>)
    open()
    fireEvent.pointerDown(control('Select R10 chip'))
    expect(screen.getByRole('group', { name: 'Chip denominations' })).toBeTruthy()
    const outside = control('Outside the picker')
    outside.focus()
    fireEvent.pointerDown(outside)
    expect(screen.queryByRole('group', { name: 'Chip denominations' })).toBeNull()
    expect(document.activeElement).toBe(outside)
    expect(input.onSelectChip).not.toHaveBeenCalled()
  })

  it('disables every command while a request is in flight and does not call its handlers', () => {
    const input = props({ disabled: true })
    render(<CrapsBetControls {...input}/> )
    const buttons = Array.from(picker().parentElement!.querySelectorAll('button'))
    expect(buttons).toHaveLength(5)
    for (const button of buttons) {
      expect(button.disabled).toBe(true)
      fireEvent.click(button)
    }
    expect(screen.queryByRole('group', { name: 'Chip denominations' })).toBeNull()
    expect(input.onSelectChip).not.toHaveBeenCalled()
    expect(input.onUndo).not.toHaveBeenCalled()
    expect(input.onClear).not.toHaveBeenCalled()
  })

  it('closes an already open tray when the game becomes disabled', () => {
    const input = props()
    const view = render(<CrapsBetControls {...input}/> )
    open()
    view.rerender(<CrapsBetControls {...input} disabled/>)
    expect(screen.queryByRole('group', { name: 'Chip denominations' })).toBeNull()
    expect(control('Chip options, selected R2').getAttribute('aria-expanded')).toBe('false')
    expect(control('Chip options, selected R2').disabled).toBe(true)
    fireEvent.keyDown(document, { key: 'Escape' })
    expect(input.onSelectChip).not.toHaveBeenCalled()
  })

  it('keeps an empty picker safe while allowing independently available Undo and Clear', () => {
    const input = props({ denominations: [] })
    render(<CrapsBetControls {...input}/> )
    expect(picker().querySelectorAll('img')).toHaveLength(0)
    for (const name of ['Increase chip value', 'Chip options, selected R2', 'Decrease chip value']) {
      expect(control(name).disabled).toBe(true)
      fireEvent.click(control(name))
    }
    expect(control('Undo last chip').disabled).toBe(false)
    expect(control('Clear bets').disabled).toBe(false)
    fireEvent.click(control('Undo last chip'))
    fireEvent.click(control('Clear bets'))
    expect(input.onUndo).toHaveBeenCalledOnce()
    expect(input.onClear).toHaveBeenCalledOnce()
    expect(input.onSelectChip).not.toHaveBeenCalled()
  })

  it('honours Undo and Clear availability separately without changing chip selection', () => {
    const input = props({ canUndo: false, canClear: true })
    const view = render(<CrapsBetControls {...input}/> )
    expect(control('Undo last chip').disabled).toBe(true)
    expect(control('Clear bets').disabled).toBe(false)
    fireEvent.click(control('Undo last chip'))
    fireEvent.click(control('Clear bets'))
    expect(input.onUndo).not.toHaveBeenCalled()
    expect(input.onClear).toHaveBeenCalledOnce()
    view.rerender(<CrapsBetControls {...input} canUndo canClear={false}/>)
    fireEvent.click(control('Undo last chip'))
    fireEvent.click(control('Clear bets'))
    expect(input.onUndo).toHaveBeenCalledOnce()
    expect(input.onClear).toHaveBeenCalledOnce()
    expect(input.onSelectChip).not.toHaveBeenCalled()
  })
})
