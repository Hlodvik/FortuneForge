import { lazy, Suspense, useEffect, useState } from 'react'
import { useAuthenticatedAccount } from '../../features/account/useAuthenticatedAccount'
import type { SlotRouteDefinition } from '../../games/slots'
import type { SlotGameManifest } from '../../games/slots/shared/slotGameManifest'
import { AuthenticatedRouteState } from './AuthenticatedRouteState'

const SlotsPage = lazy(() => import('../../pages/slots/SlotsPage').then(module => ({ default: module.SlotsPage })))
const NeonNightsHost = lazy(() => import('./NeonNightsHost').then(module => ({ default: module.NeonNightsHost })))

export function SlotGameRoute({
  definition,
  pathname,
  onSpinStateChange,
}: {
  definition: SlotRouteDefinition
  pathname: string
  onSpinStateChange?: (isSpinning: boolean) => void
}) {
  if (definition.id === 'neon-nights') {
    return <Suspense fallback={<AuthenticatedRouteState error={null} loadingLabel="Opening Neon Nights…" errorTitle="Neon Nights could not be opened." onRetry={() => window.location.reload()} />}>
      {pathname === definition.demoPath ? <NeonNightsHost demoMode onSpinStateChange={onSpinStateChange} /> : <AuthenticatedNeonNights returnPath={pathname} onSpinStateChange={onSpinStateChange} />}
    </Suspense>
  }
  return <LegacySlotGameRoute definition={definition} pathname={pathname} onSpinStateChange={onSpinStateChange} />
}

function LegacySlotGameRoute({ definition, pathname, onSpinStateChange }: { definition: SlotRouteDefinition; pathname: string; onSpinStateChange?: (isSpinning: boolean) => void }) {
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
    return <Suspense fallback={<AuthenticatedRouteState error={null} loadingLabel={`Opening ${definition.shortTitle}…`} errorTitle={`${definition.title} could not be opened.`} onRetry={() => window.location.reload()} />}><SlotsPage demoMode experienceSet={manifest.experience} onSpinStateChange={onSpinStateChange} /></Suspense>
  }

  return <AuthenticatedSlotGame manifest={manifest} onSpinStateChange={onSpinStateChange} returnPath={pathname} />
}

function AuthenticatedSlotGame({
  manifest,
  onSpinStateChange,
  returnPath,
}: {
  manifest: SlotGameManifest
  onSpinStateChange?: (isSpinning: boolean) => void
  returnPath: string
}) {
  const { account, error, isLoading, reload } = useAuthenticatedAccount(returnPath)

  if (isLoading || account === null) {
    return <AuthenticatedRouteState error={error} loadingLabel="Opening Fortune Slots…" errorTitle="Fortune Slots could not be opened." onRetry={reload} />
  }
  return <Suspense fallback={<AuthenticatedRouteState error={null} loadingLabel="Opening Fortune Slots…" errorTitle="Fortune Slots could not be opened." onRetry={reload} />}><SlotsPage account={account} experienceSet={manifest.experience} onSpinStateChange={onSpinStateChange} /></Suspense>
}

function AuthenticatedNeonNights({ returnPath, onSpinStateChange }: { returnPath: string; onSpinStateChange?: (isSpinning: boolean) => void }) {
  const { account, error, isLoading, reload } = useAuthenticatedAccount(returnPath)
  if (isLoading || account === null) return <AuthenticatedRouteState error={error} loadingLabel="Opening Neon Nights…" errorTitle="Neon Nights could not be opened." onRetry={reload} />
  return <NeonNightsHost account={account} onSpinStateChange={onSpinStateChange} />
}
