import { lazy, Suspense, type ComponentType } from 'react'
import { useAuthenticatedAccount } from '../../features/account/useAuthenticatedAccount'
import type { SlotRouteDefinition } from '../../games/slots'
import type { PackagedSlotHostProps } from './PackagedSlotHost'
import { AuthenticatedRouteState } from './AuthenticatedRouteState'
import { isGameUnderMaintenance } from '../../features/catalog/gameMaintenance'
import { GameMaintenanceState } from '../../features/catalog/GameMaintenanceState'

// The platform selects a package host. Each package implements its own game.
export const standaloneSlotHosts: Readonly<Partial<Record<SlotRouteDefinition['id'], ComponentType<PackagedSlotHostProps>>>> = {
  'pirates-fortune': lazy(() => import('./PiratesFortuneHost').then(module => ({ default: module.PiratesFortuneHost }))),
  'gods-of-olympus': lazy(() => import('./GodsOfOlympusHost').then(module => ({ default: module.GodsOfOlympusHost }))),
  'neon-nights': lazy(() => import('./NeonNightsHost').then(module => ({ default: module.NeonNightsHost }))),
  'dragon-hoard': lazy(() => import('./MidnightVaultHost').then(module => ({ default: module.MidnightVaultHost }))),
  'gold-foundry': lazy(() => import('./GoldFoundryHost').then(module => ({ default: module.GoldFoundryHost }))),
  'high-noon-fortune': lazy(() => import('./FrontierFortuneHost').then(module => ({ default: module.FrontierFortuneHost }))),
  'crooked-road': lazy(() => import('./CrookedRoadHost').then(module => ({ default: module.CrookedRoadHost }))),
  'arcane-archives': lazy(() => import('./ArcaneArchivesHost').then(module => ({ default: module.ArcaneArchivesHost }))),
  'pharaohs-ascent': lazy(() => import('./PharaohsAscentHost').then(module => ({ default: module.PharaohsAscentHost }))),
}

type SlotGameRouteProps = {
  definition: SlotRouteDefinition
  pathname: string
  onSpinStateChange?: (isSpinning: boolean) => void
}

export function SlotGameRoute(props: SlotGameRouteProps) {
  if (isGameUnderMaintenance(props.definition)) return <GameMaintenanceState title={props.definition.title} />
  const Host = standaloneSlotHosts[props.definition.id]
  if (!Host) {
    return <AuthenticatedRouteState error="This game is currently unavailable."
      loadingLabel={`Opening ${props.definition.shortTitle}…`}
      errorTitle={`${props.definition.title} could not be opened.`}
      onRetry={() => window.location.reload()} />
  }
  return <Suspense fallback={<AuthenticatedRouteState error={null}
    loadingLabel={`Opening ${props.definition.shortTitle}…`}
    errorTitle={`${props.definition.title} could not be opened.`}
    onRetry={() => window.location.reload()} />}>
    <AuthenticatedPackage {...props} Host={Host} />
  </Suspense>
}

function AuthenticatedPackage({ pathname, definition, onSpinStateChange, Host }: SlotGameRouteProps & {
  Host: ComponentType<PackagedSlotHostProps>
}) {
  const { account, error, isLoading, reload } = useAuthenticatedAccount(pathname)
  if (isLoading || account === null) return <AuthenticatedRouteState error={error}
    loadingLabel={`Opening ${definition.shortTitle}…`}
    errorTitle={`${definition.title} could not be opened.`} onRetry={reload} />
  return <Host account={account} onSpinStateChange={onSpinStateChange} />
}
