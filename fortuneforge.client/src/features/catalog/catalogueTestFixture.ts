import { SLOT_ROUTE_DEFINITIONS } from '../../games/slots/routeRegistry'
import { notifyCatalogueChanged } from './catalogVisibility'

/** Explicit verified catalogue for static-render tests that cannot run fetch effects. */
export function useVerifiedTestCatalogue() {
  const routes = [
    '/cards/blackjack', '/cards/texas-holdem', '/cards/solitaire', '/cards/hearts',
    '/cards/three-card-poker', '/cards/house-holdem', '/games/video-poker',
    '/games/baccarat', '/games/casino-war', '/games/keno', '/games/sic-bo',
    '/games/roulette', '/games/craps', '/games/liars-dice', '/games/asteroids',
    '/games/flappy', '/games/2048', '/games/drop-merge', '/games/snake',
    ...SLOT_ROUTE_DEFINITIONS.map(game => game.playPath!).filter(Boolean),
  ]
  notifyCatalogueChanged({ version: 1, games: [
    ...routes.map(route => ({ id: route, route, visible: true })),
    { id: 'horse-flight', route: '/games/horse-flight', visible: false },
  ] })
}
