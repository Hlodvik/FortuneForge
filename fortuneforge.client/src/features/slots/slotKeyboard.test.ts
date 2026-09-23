import { describe, expect, it } from 'vitest'
import { getSlotKeyboardAction } from './slotKeyboard'

const ready = {
  hasOpenDialog: false,
  isEditableTarget: false,
  modified: false,
  repeated: false,
}

describe('slot keyboard controls', () => {
  it('maps the shared cabinet shortcuts', () => {
    expect(getSlotKeyboardAction(' ', ready)).toBe('spin-or-stop')
    expect(getSlotKeyboardAction('ArrowLeft', ready)).toBe('decrease-wager')
    expect(getSlotKeyboardAction('ArrowRight', ready)).toBe('increase-wager')
    expect(getSlotKeyboardAction('M', ready)).toBe('toggle-mute')
    expect(getSlotKeyboardAction('?', ready)).toBe('open-help')
  })

  it('does not steal input from dialogs, controls, modified keys, or key repeat', () => {
    for (const context of [
      { ...ready, hasOpenDialog: true },
      { ...ready, isEditableTarget: true },
      { ...ready, modified: true },
      { ...ready, repeated: true },
    ]) {
      expect(getSlotKeyboardAction(' ', context)).toBeNull()
    }
  })
})
