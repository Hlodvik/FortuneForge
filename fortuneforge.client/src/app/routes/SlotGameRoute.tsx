import { lazy, Suspense, useEffect, useState, type ReactNode } from 'react'
import { useAuthenticatedAccount } from '../../features/account/useAuthenticatedAccount'
import type { SlotRouteDefinition } from '../../games/slots'
import { adoptPackagedSlotGameManifest, type PackagedSlotGameManifestEnvelope, type SlotGameManifest } from '../../games/slots/shared/slotGameManifest'
import { useGameRelease } from '../../features/game-releases/useGameRelease'
import { AuthenticatedRouteState } from './AuthenticatedRouteState'

const SlotsPage = lazy(() => import('../../pages/slots/SlotsPage').then(module => ({ default: module.SlotsPage })))
const NeonNightsHost = lazy(() => import('./NeonNightsHost').then(module => ({ default: module.NeonNightsHost })))
const MidnightVaultHost = lazy(() => import('./MidnightVaultHost').then(module => ({ default: module.MidnightVaultHost })))
const GoldFoundryHost = lazy(() => import('./GoldFoundryHost').then(module => ({ default: module.GoldFoundryHost })))

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
  if (definition.id === 'dragon-hoard') {
    return <Suspense fallback={<AuthenticatedRouteState error={null} loadingLabel="Opening Midnight Vault…" errorTitle="Midnight Vault could not be opened." onRetry={() => window.location.reload()} />}>
      {pathname === definition.demoPath ? <MidnightVaultHost demoMode onSpinStateChange={onSpinStateChange} /> : <AuthenticatedMidnightVault returnPath={pathname} onSpinStateChange={onSpinStateChange} />}
    </Suspense>
  }
  if (definition.id === 'gold-foundry') {
    return <Suspense fallback={<AuthenticatedRouteState error={null} loadingLabel="Opening Gold Foundry…" errorTitle="Gold Foundry could not be opened." onRetry={() => window.location.reload()} />}>
      {pathname === definition.demoPath ? <GoldFoundryHost demoMode onSpinStateChange={onSpinStateChange} /> : <AuthenticatedGoldFoundry returnPath={pathname} onSpinStateChange={onSpinStateChange} />}
    </Suspense>
  }
  return <LegacySlotGameRoute definition={definition} pathname={pathname} onSpinStateChange={onSpinStateChange} />
}

type ClassicSlotRouteProps = {
  definition: SlotRouteDefinition
  pathname: string
  onSpinStateChange?: (isSpinning: boolean) => void
}

type ClassicSlotBrowserProps = {
  renderSlot: (manifest: PackagedSlotGameManifestEnvelope) => ReactNode
}

function LegacySlotGameRoute(props: ClassicSlotRouteProps) {
  const { definition } = props
  const { state, retry } = useGameRelease<ClassicSlotBrowserProps>(definition.gameReleaseId ?? definition.id)

  if (state.status === 'local') return <BundledSlotGameRoute {...props} />
  if (state.status === 'ready') {
    const { Game } = state
    return <Game renderSlot={manifest => {
      if (manifest?.id !== definition.id || manifest.routes?.play !== definition.playPath ||
        manifest.routes.demo !== definition.demoPath) {
        return <AuthenticatedRouteState error="The published game does not match this route."
          loadingLabel={'Opening ' + definition.shortTitle + '…'}
          errorTitle={definition.title + ' could not be opened.'} onRetry={retry} />
      }
      return <ResolvedSlotGameRoute {...props} manifest={adoptPackagedSlotGameManifest(manifest)} />
    }} />
  }
  return <AuthenticatedRouteState error={state.status === 'error' ? state.error : null}
    loadingLabel={'Opening ' + definition.shortTitle + '…'}
    errorTitle={definition.title + ' could not be opened.'} onRetry={retry} />
}

function BundledSlotGameRoute(props: ClassicSlotRouteProps) {
  const { definition } = props
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

  return <ResolvedSlotGameRoute {...props} manifest={manifest} />
}

function ResolvedSlotGameRoute({ definition, pathname, onSpinStateChange, manifest }: ClassicSlotRouteProps & { manifest: SlotGameManifest }) {
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

function AuthenticatedMidnightVault({ returnPath, onSpinStateChange }: { returnPath: string; onSpinStateChange?: (isSpinning: boolean) => void }) {
  const { account, error, isLoading, reload } = useAuthenticatedAccount(returnPath)
  if (isLoading || account === null) return <AuthenticatedRouteState error={error} loadingLabel="Opening Midnight Vault…" errorTitle="Midnight Vault could not be opened." onRetry={reload} />
  return <MidnightVaultHost account={account} onSpinStateChange={onSpinStateChange} />
}

function AuthenticatedGoldFoundry({ returnPath, onSpinStateChange }: { returnPath: string; onSpinStateChange?: (isSpinning: boolean) => void }) {
  const { account, error, isLoading, reload } = useAuthenticatedAccount(returnPath)
  if (isLoading || account === null) return <AuthenticatedRouteState error={error} loadingLabel="Opening Gold Foundry…" errorTitle="Gold Foundry could not be opened." onRetry={reload} />
  return <GoldFoundryHost account={account} onSpinStateChange={onSpinStateChange} />
}
