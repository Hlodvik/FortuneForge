import { useEffect, useState } from 'react'
import { useAuthenticatedAccount } from '../../features/account/useAuthenticatedAccount'
import type { SlotRouteDefinition } from '../../games/slots'
import type { SlotGameManifest } from '../../games/slots/shared/slotGameManifest'
import { SlotsPage } from '../../pages/slots/SlotsPage'
import { AuthenticatedRouteState } from './AuthenticatedRouteState'

export function SlotGameRoute({
  definition,
  pathname,
  onSpinStateChange,
}: {
  definition: SlotRouteDefinition
  pathname: string
  onSpinStateChange: (isSpinning: boolean) => void
}) {
  const [attempt, setAttempt] = useState(0)
  const [manifest, setManifest] = useState<SlotGameManifest | null>(null)
  const [loadError, setLoadError] = useState<string | null>(null)

  useEffect(() => {
    let active = true
    setManifest(null)
    setLoadError(null)
    void definition.load().then(
      (loadedManifest) => {
        if (active) setManifest(loadedManifest)
      },
      (error: unknown) => {
        if (active) setLoadError(error instanceof Error ? error.message : 'The game resources could not be loaded.')
      },
    )
    return () => { active = false }
  }, [attempt, definition])

  if (manifest === null) {
    return (
      <AuthenticatedRouteState
        error={loadError}
        loadingLabel={`Opening ${definition.shortTitle}…`}
        errorTitle={`${definition.title} could not be opened.`}
        onRetry={() => setAttempt((value) => value + 1)}
      />
    )
  }

  if (pathname === definition.demoPath) {
    return <SlotsPage demoMode experienceSet={manifest.experience} onSpinStateChange={onSpinStateChange} />
  }

  return <AuthenticatedSlotGame manifest={manifest} onSpinStateChange={onSpinStateChange} returnPath={pathname} />
}

function AuthenticatedSlotGame({
  manifest,
  onSpinStateChange,
  returnPath,
}: {
  manifest: SlotGameManifest
  onSpinStateChange: (isSpinning: boolean) => void
  returnPath: string
}) {
  const { account, error, isLoading, reload } = useAuthenticatedAccount(returnPath)

  if (isLoading || account === null) {
    return <AuthenticatedRouteState error={error} loadingLabel="Opening Fortune Slots…" errorTitle="Fortune Slots could not be opened." onRetry={reload} />
  }
  return <SlotsPage account={account} experienceSet={manifest.experience} onSpinStateChange={onSpinStateChange} />
}
