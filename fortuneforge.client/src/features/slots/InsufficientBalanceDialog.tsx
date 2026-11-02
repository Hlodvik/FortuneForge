import type { RefObject } from 'react'
import { createPortal } from 'react-dom'
import { SlotDialogCloseIcon } from './components/SlotDialogCloseIcon'
import { useSlotDialog, type SlotDialogTheme } from './hooks/useSlotDialog'
import { formatRand } from './slotPagePresentation'

type InsufficientBalanceDialogProps = {
  isOpen: boolean
  closeButtonRef: RefObject<HTMLButtonElement | null>
  selectedWager: number
  balance: number
  recoveryHref: string
  recoveryLabel: string
  onClose: () => void
  theme?: SlotDialogTheme
}

export function InsufficientBalanceDialog({
  isOpen,
  closeButtonRef,
  selectedWager,
  balance,
  recoveryHref,
  recoveryLabel,
  onClose,
  theme,
}: InsufficientBalanceDialogProps) {
  const dialogRef = useSlotDialog(isOpen, onClose, closeButtonRef)
  if (!isOpen) return null

  const content = (
    <div
      data-slot-dialog-theme={theme}
      className="fortune-prompt__backdrop"
      role="presentation"
      onMouseDown={(event) => {
        if (event.target === event.currentTarget) {
          onClose()
        }
      }}
    >
      <section
        ref={dialogRef}
        tabIndex={-1}
        data-slot-dialog-theme={theme}
        className="fortune-prompt reload-prompt"
        role="dialog"
        aria-modal="true"
        aria-labelledby="reload-prompt-title"
      >
        <button
          ref={closeButtonRef}
          className="fortune-prompt__close"
          type="button"
          aria-label="Close insufficient fortune message"
          onClick={() => onClose()}
        >
          <SlotDialogCloseIcon />
        </button>
        <div className="reload-prompt__icon" aria-hidden="true">!</div>
        <p className="fortune-prompt__eyebrow">More fortune needed</p>
        <h2 id="reload-prompt-title">Not enough fortune</h2>
        <p className="reload-prompt__copy">
          This spin costs {formatRand(selectedWager)}, but your current balance is {formatRand(balance)}.
          Choose a smaller wager to continue.
        </p>
        <div className="reload-prompt__actions">
          <button className="reload-prompt__primary" type="button" onClick={() => onClose()}>
            Choose another wager
          </button>
          <a href={recoveryHref}>{recoveryLabel}</a>
        </div>
      </section>
    </div>
  )
  return theme && typeof document !== 'undefined' ? createPortal(content, document.body) : content
}
