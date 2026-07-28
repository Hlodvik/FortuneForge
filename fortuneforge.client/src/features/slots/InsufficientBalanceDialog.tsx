import type { RefObject } from 'react'
import { formatRand } from './slotPagePresentation'

type InsufficientBalanceDialogProps = {
  isOpen: boolean
  closeButtonRef: RefObject<HTMLButtonElement | null>
  selectedWager: number
  balance: number
  onClose: () => void
}

export function InsufficientBalanceDialog({
  isOpen,
  closeButtonRef,
  selectedWager,
  balance,
  onClose,
}: InsufficientBalanceDialogProps) {
  if (!isOpen) return null

  return (
    <div
      className="fortune-prompt__backdrop"
      role="presentation"
      onMouseDown={(event) => {
        if (event.target === event.currentTarget) {
          onClose()
        }
      }}
    >
      <section
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
          ×
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
          <a href="/home/credits">Add balance</a>
        </div>
      </section>
    </div>
  )
}
