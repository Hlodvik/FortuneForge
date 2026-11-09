import type { ReactNode, RefObject } from 'react'
import { createPortal } from 'react-dom'
import { SlotDialogCloseIcon } from './components/SlotDialogCloseIcon'
import { useSlotDialog, type SlotDialogTheme } from './hooks/useSlotDialog'

export type WinHelpDialogProps = {
  children: ReactNode
  isOpen: boolean
  closeButtonRef: RefObject<HTMLButtonElement | null>
  onClose: () => void
  theme?: SlotDialogTheme
  title?: string
  eyebrow?: string
}

/** A dialog frame only. Help content and game rules belong to the game. */
export function WinHelpDialog({ children, isOpen, closeButtonRef, onClose, theme,
  title = 'What counts as a win?', eyebrow = 'Fortune guide' }: WinHelpDialogProps) {
  const dialogRef = useSlotDialog(isOpen, onClose, closeButtonRef)
  if (!isOpen) return null
  const content = <div data-slot-dialog-theme={theme} data-slot-dialog-custom={theme ? true : undefined} className="win-help__backdrop" role="presentation"
    onMouseDown={event => { if (event.target === event.currentTarget) onClose() }}>
    <section ref={dialogRef} tabIndex={-1} data-slot-dialog-theme={theme} data-slot-dialog-custom={theme ? true : undefined} className="win-help" role="dialog"
      aria-modal="true" aria-labelledby="win-help-title">
      <button ref={closeButtonRef} className="win-help__close" type="button" aria-label="Close win guide" onClick={onClose}>
        <SlotDialogCloseIcon />
      </button>
      <p className="win-help__eyebrow">{eyebrow}</p><h2 id="win-help-title">{title}</h2>{children}
    </section>
  </div>
  return typeof document !== 'undefined' ? createPortal(content, document.body) : content
}
