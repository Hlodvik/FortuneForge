type SpinLeverProps = {
  disabled?: boolean
  isSpinning?: boolean
  onSpin: () => void
}

export function SpinLever({ disabled = false, isSpinning = false, onSpin }: SpinLeverProps) {
  return (
    <button
      className={`spin-lever${isSpinning ? ' spin-lever--active' : ''}`}
      type="button"
      onClick={onSpin}
      disabled={disabled || isSpinning}
      aria-label={isSpinning ? 'Spin in progress' : 'Spin the reels'}
    >
      <span className="spin-lever__knob" aria-hidden="true" />
      <span className="spin-lever__shaft" aria-hidden="true" />
      <span className="spin-lever__label">{isSpinning ? 'Spinning' : 'Spin'}</span>
    </button>
  )
}
