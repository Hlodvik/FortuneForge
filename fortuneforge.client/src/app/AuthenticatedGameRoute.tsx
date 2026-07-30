import { GameLibraryPage } from '../features/games/GameLibraryPage'
import { useAuthenticatedAccount } from '../features/landing/useAuthenticatedAccount'
import { SlotsPage } from '../features/slots/SlotsPage'
import type { SlotExperienceSet } from '../features/slots/config/slotExperienceSets'

function AuthenticatedRouteState({
  error,
  loadingLabel,
  errorTitle,
  onRetry,
}: {
  error: string | null
  loadingLabel: string
  errorTitle: string
  onRetry: () => void
}) {
  return (
    <div className="player-page">
      <main className="player-main">
        {error === null ? (
          <div className="player-state" role="status">
            {loadingLabel}
          </div>
        ) : (
          <div className="player-state player-state--error" role="alert">
            <strong>{errorTitle}</strong>
            <span>{error}</span>
            <button
              className="landing-button landing-button--secondary"
              type="button"
              onClick={onRetry}
            >
              Try again
            </button>
            <a href="/home">Return home</a>
          </div>
        )}
      </main>
    </div>
  )
}

export function AuthenticatedSlotsRoute({
  experienceSet,
  onSpinStateChange,
  returnPath,
}: {
  experienceSet: SlotExperienceSet
  onSpinStateChange: (isSpinning: boolean) => void
  returnPath: string
}) {
  const { account, error, isLoading, reload } =
    useAuthenticatedAccount(returnPath)

  if (isLoading || account === null) {
    return (
      <AuthenticatedRouteState
        error={error}
        loadingLabel="Opening Fortune Slots…"
        errorTitle="Fortune Slots could not be opened."
        onRetry={reload}
      />
    )
  }

  return (
    <SlotsPage
      account={account}
      experienceSet={experienceSet}
      onSpinStateChange={onSpinStateChange}
    />
  )
}

export function DemoSlotsRoute({
  experienceSet,
  onSpinStateChange,
}: {
  experienceSet: SlotExperienceSet
  onSpinStateChange: (isSpinning: boolean) => void
}) {
  return (
    <SlotsPage
      demoMode
      experienceSet={experienceSet}
      onSpinStateChange={onSpinStateChange}
    />
  )
}

export function AuthenticatedGameLibraryRoute() {
  const { account, error, isLoading, reload } =
    useAuthenticatedAccount('/slots')

  if (isLoading || account === null) {
    return (
      <AuthenticatedRouteState
        error={error}
        loadingLabel="Opening the slot collection…"
        errorTitle="The slot collection could not be opened."
        onRetry={reload}
      />
    )
  }

  return <GameLibraryPage account={account} />
}
