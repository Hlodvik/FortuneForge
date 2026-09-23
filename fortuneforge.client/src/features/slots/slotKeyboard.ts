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
  if (
    context.hasOpenDialog ||
    context.isEditableTarget ||
    context.modified ||
    context.repeated
  ) {
    return null
  }

  switch (key) {
    case ' ':
    case 'Spacebar':
      return 'spin-or-stop'
    case 'ArrowLeft':
      return 'decrease-wager'
    case 'ArrowRight':
      return 'increase-wager'
    case 'm':
    case 'M':
      return 'toggle-mute'
    case '?':
      return 'open-help'
    default:
      return null
  }
}
