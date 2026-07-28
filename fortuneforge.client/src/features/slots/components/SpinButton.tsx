import { useState } from 'react'

type SpinButtonProps = {
  disabled?: boolean
  isSpinning?: boolean
  isStopRequested?: boolean
  onSpin: () => void
}

export function SpinButton({
  disabled = false,
  isSpinning = false,
  isStopRequested = false,
  onSpin,
}: SpinButtonProps) {
  const isActive = isSpinning || isStopRequested
  const [spinBurstKey, setSpinBurstKey] = useState(0)

  const handleClick = () => {
    setSpinBurstKey((current) => current + 1)
    onSpin()
  }

  return (
    <button
      className={`spin-button${isActive ? ' spin-button--active' : ''}${isStopRequested ? ' spin-button--stopping' : ''}`}
      type="button"
      onClick={handleClick}
      disabled={disabled && !isActive}
      aria-pressed={isActive}
      aria-label={isActive ? 'Stop the spin' : 'Spin the reels'}
    >
      <svg
        key={spinBurstKey}
        className={`spin-button__icon${spinBurstKey > 0 && !isActive ? ' spin-button__icon--burst' : ''}`}
        viewBox="0 0 512 512"
        aria-hidden="true"
      >
        {isActive
          ? <rect className="spin-button__stop-icon" x="168" y="168" width="176" height="176" rx="24" />
          : (
            <path
              className="spin-button__arrow-icon"
              d="M105.1 202.6c7.7-21.8 20.2-42.3 37.8-59.8 62.5-62.5 163.8-62.5 226.3 0l17.1 17.2H336c-17.7 0-32 14.3-32 32s14.3 32 32 32h128c17.7 0 32-14.3 32-32V64c0-17.7-14.3-32-32-32s-32 14.3-32 32v51.2l-17.6-17.6c-87.5-87.5-229.3-87.5-316.8 0C73.2 122 55.6 150.7 44.8 181.4c-5.9 16.7 2.9 34.9 19.5 40.8s34.9-2.9 40.8-19.6ZM39 289.3c-5 1.5-9.8 4.2-13.7 8.2-4 4-6.7 8.8-8.1 14-.8 2.8-1.2 5.6-1.2 8.5v128c0 17.7 14.3 32 32 32s32-14.3 32-32v-51.1l17.6 17.5c87.5 87.4 229.3 87.4 316.7 0 24.4-24.4 42.1-53.1 52.9-83.7 5.9-16.7-2.9-34.9-19.5-40.8s-34.9 2.9-40.8 19.5c-7.7 21.8-20.2 42.3-37.8 59.8-62.5 62.5-163.8 62.5-226.3 0l-17.2-17.2H176c17.7 0 32-14.3 32-32s-14.3-32-32-32H48.4c-3.2 0-6.3.4-9.4 1.3Z"
            />
          )}
      </svg>
      <strong className="spin-button__label">
        {isStopRequested ? 'Stopping' : isSpinning ? 'Stop' : 'Spin'}
      </strong>
    </button>
  )
}
