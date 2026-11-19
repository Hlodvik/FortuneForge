// Edited: 2026-11-18T22:49:02-06:00 | Computer: Vinnabok
import { lazy, Suspense, type ReactNode } from 'react'
import { RouteStateLayout } from './RouteStateLayout'
import { resolveSiteRoute, SiteNotFoundPage } from './SiteRoutes'
import { RouteErrorBoundary, RouteErrorState, RouteLoadingState } from './RouteBoundary'
import { GameLoadingScreen } from '../features/game-loading/GameLoadingScreen'
import { GameLoadingRouteProvider } from '../features/game-loading/GameLoadingRouteContext'
import { resolveGameLoadingEntry } from '../features/game-loading/gameLoadingRegistry'
import type { SlotRouteDefinition } from '../games/slots'

const AuthenticatedBlackjackRoute = lazy(() => import('./routes/BlackjackRoute').then((module) => ({ default: module.AuthenticatedBlackjackRoute })))
const AuthenticatedThreeCardPokerRoute = lazy(() => import('./routes/ThreeCardPokerRoute').then((module) => ({ default: module.AuthenticatedThreeCardPokerRoute })))
const AuthenticatedHouseHoldemRoute = lazy(() => import('./routes/HouseHoldemRoute').then((module) => ({ default: module.AuthenticatedHouseHoldemRoute })))
const AuthenticatedTexasHoldemRoute = lazy(() => import('./routes/TexasHoldemRoute').then((module) => ({ default: module.AuthenticatedTexasHoldemRoute })))
const AuthenticatedSolitaireRoute = lazy(() => import('./routes/SolitaireRoute').then((module) => ({ default: module.AuthenticatedSolitaireRoute })))
const AuthenticatedFreeSolitaireRoute = lazy(() => import('./routes/FreeSolitaireRoute').then((module) => ({ default: module.AuthenticatedFreeSolitaireRoute })))
const AuthenticatedAsteroidsRoute = lazy(() => import('./routes/AsteroidsRoute').then((module) => ({ default: module.AuthenticatedAsteroidsRoute })))
const AuthenticatedTwentyFortyEightRoute = lazy(() => import('./routes/TwentyFortyEightRoute').then((module) => ({ default: module.AuthenticatedTwentyFortyEightRoute })))
const AuthenticatedDropMergeRoute = lazy(() => import('./routes/DropMergeRoute').then((module) => ({ default: module.AuthenticatedDropMergeRoute })))
const AuthenticatedBaccaratRoute = lazy(() => import('./routes/BaccaratRoute').then((module) => ({ default: module.AuthenticatedBaccaratRoute })))
const AuthenticatedDragonTigerRoute = lazy(() => import('./routes/DragonTigerRoute').then((module) => ({ default: module.AuthenticatedDragonTigerRoute })))
const AuthenticatedHiLoRoute = lazy(() => import('./routes/HiLoRoute').then((module) => ({ default: module.AuthenticatedHiLoRoute })))
const AuthenticatedAndarBaharRoute = lazy(() => import('./routes/AndarBaharRoute').then((module) => ({ default: module.AuthenticatedAndarBaharRoute })))
const AuthenticatedMinesRoute = lazy(() => import('./routes/MinesRoute').then((module) => ({ default: module.AuthenticatedMinesRoute })))
const AuthenticatedCasinoWarRoute = lazy(() => import('./routes/CasinoWarRoute').then((module) => ({ default: module.AuthenticatedCasinoWarRoute })))
const AuthenticatedKenoRoute = lazy(() => import('./routes/KenoRoute').then((module) => ({ default: module.AuthenticatedKenoRoute })))
const AuthenticatedSicBoRoute = lazy(() => import('./routes/SicBoRoute').then((module) => ({ default: module.AuthenticatedSicBoRoute })))
const AuthenticatedFlappyRoute = lazy(() => import('./routes/FlappyRoute').then((module) => ({ default: module.AuthenticatedFlappyRoute })))
const AuthenticatedHorseFlightRoute = lazy(() => import('./routes/HorseFlightRoute').then((module) => ({ default: module.AuthenticatedHorseFlightRoute })))
const AuthenticatedSnakeRoute = lazy(() => import('./routes/SnakeRoute').then((module) => ({ default: module.AuthenticatedSnakeRoute })))
const AuthenticatedVideoPokerRoute = lazy(() => import('./routes/VideoPokerRoute').then((module) => ({ default: module.AuthenticatedVideoPokerRoute })))
const AuthenticatedHeartsRoute = lazy(() => import('./routes/HeartsRoute').then((module) => ({ default: module.AuthenticatedHeartsRoute })))
const AuthenticatedRouletteRoute = lazy(() => import('./routes/RouletteRoute').then((module) => ({ default: module.AuthenticatedRouletteRoute })))
const AuthenticatedCrapsRoute = lazy(() => import('./routes/CrapsRoute').then((module) => ({ default: module.AuthenticatedCrapsRoute })))
const AuthenticatedLiarsDiceRoute = lazy(() => import('./routes/LiarsDiceRoute').then((module) => ({ default: module.AuthenticatedLiarsDiceRoute })))
const JadeDragonRoute = lazy(() => import('./routes/JadeDragonRoute').then((module) => ({ default: module.JadeDragonRoute })))
const SlotGameRoute = lazy(() => import('./routes/SlotGameRoute').then((module) => ({ default: module.SlotGameRoute })))

export function AppRoutes({
  pathname,
  slotRoute,
  onSpinStateChange,
}: {
  pathname: string
  slotRoute: SlotRouteDefinition | null
  onSpinStateChange?: (isSpinning: boolean) => void
}) {
  const loadingEntry = resolveGameLoadingEntry(pathname)
  let route: ReactNode

  const siteRoute = resolveSiteRoute(pathname)
  if (siteRoute !== null) route = siteRoute
  else if (pathname === '/games/asteroids') route = <AuthenticatedAsteroidsRoute />
  else if (pathname === '/games/2048') route = <AuthenticatedTwentyFortyEightRoute />
  else if (pathname === '/games/drop-merge') route = <AuthenticatedDropMergeRoute />
  else if (pathname === '/games/baccarat') route = <AuthenticatedBaccaratRoute />
  else if (pathname === '/games/dragon-tiger') route = <AuthenticatedDragonTigerRoute />
  else if (pathname === '/games/hi-lo') route = <AuthenticatedHiLoRoute />
  else if (pathname === '/games/andar-bahar') route = <AuthenticatedAndarBaharRoute />
  else if (pathname === '/games/mines') route = <AuthenticatedMinesRoute />
  else if (pathname === '/games/casino-war') route = <AuthenticatedCasinoWarRoute />
  else if (pathname === '/games/keno') route = <AuthenticatedKenoRoute />
  else if (pathname === '/games/sic-bo') route = <AuthenticatedSicBoRoute />
  else if (pathname === '/games/flappy') route = <AuthenticatedFlappyRoute />
  else if (pathname === '/games/horse-flight') route = <AuthenticatedHorseFlightRoute />
  else if (pathname === '/games/snake') route = <AuthenticatedSnakeRoute />
  else if (pathname === '/games/video-poker') route = <AuthenticatedVideoPokerRoute />
  else if (pathname === '/games/roulette') route = <AuthenticatedRouletteRoute />
  else if (pathname === '/games/craps') route = <AuthenticatedCrapsRoute />
  else if (pathname === '/games/liars-dice') route = <AuthenticatedLiarsDiceRoute />
  else if (pathname === '/cards/hearts') route = <AuthenticatedHeartsRoute />
  else if (pathname === '/cards/blackjack') route = <AuthenticatedBlackjackRoute />
  else if (pathname === '/cards/three-card-poker') route = <AuthenticatedThreeCardPokerRoute />
  else if (pathname === '/cards/house-holdem') route = <AuthenticatedHouseHoldemRoute />
  else if (pathname === '/cards/texas-holdem') route = <AuthenticatedTexasHoldemRoute />
  else if (pathname === '/cards/solitaire') route = <AuthenticatedSolitaireRoute />
  else if (pathname === '/cards/free-solitaire') route = <AuthenticatedFreeSolitaireRoute />
  else if (pathname === '/slots/jade-dragon') route = <JadeDragonRoute />
  else if (slotRoute !== null && pathname === slotRoute.playPath) {
    route = <SlotGameRoute definition={slotRoute} pathname={pathname} onSpinStateChange={onSpinStateChange} />
  }
  else route = <SiteNotFoundPage />

  return (
    <GameLoadingRouteProvider entry={loadingEntry} pathname={pathname}>
      <RouteErrorBoundary key={pathname} fallback={<RouteStateLayout pathname={pathname}><RouteErrorState /></RouteStateLayout>}>
        <Suspense fallback={pathname === '/slots/jade-dragon' ? null : <RouteStateLayout pathname={pathname} entry={loadingEntry}>{loadingEntry ? <GameLoadingScreen entry={loadingEntry} /> : <RouteLoadingState />}</RouteStateLayout>}>{route}</Suspense>
      </RouteErrorBoundary>
    </GameLoadingRouteProvider>
  )
}
