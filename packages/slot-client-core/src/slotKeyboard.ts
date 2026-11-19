// Edited: 2026-11-18T14:43:56-06:00 | Computer: Vinnabok
export type SlotKeyboardAction =
  | 'decrease-wager'
  | 'increase-wager'
  | 'open-help'
  | 'spin-or-stop'
  | 'toggle-mute'

type SlotKeyboardContext = {
  hasOpenDialog: boolean
  isEditableTarget: boolean
  modified: boolean
  repeated: boolean
}

export function getSlotKeyboardAction(
  key: string,
  context: SlotKeyboardContext,
): SlotKeyboardAction | null {
  if (context.modified || context.repeated) return null
  if (key === 'm' || key === 'M') return 'toggle-mute'
  if (context.hasOpenDialog || context.isEditableTarget) return null

  switch (key) {
    case ' ':
    case 'Spacebar':
      return 'spin-or-stop'
    case 'ArrowLeft':
      return 'decrease-wager'
    case 'ArrowRight':
      return 'increase-wager'
    case '?':
      return 'open-help'
    default:
      return null
  }
}
