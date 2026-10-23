import { afterEach, describe, expect, it, vi } from 'vitest'
import { catalogueRouteVisible } from './catalogVisibility'
afterEach(() => vi.unstubAllGlobals())
const state = { version: 3, games: [{ id: 'horse-flight', route: '/games/horse-flight', visible: false }, { id: 'hearts', route: '/cards/hearts', visible: true }, { id: 'rainbow-realm', route: '/slots/rainbow-realm', visible: true }] }
describe('catalogue visibility', () => {
  it('does not list games before configuration is verified', () => { expect(catalogueRouteVisible(null, '/cards/hearts')).toBe(false) })
  it('hides a configured game in any catalogue section', () => { expect(catalogueRouteVisible(state, '/games/horse-flight')).toBe(false) })
  it('keeps demo and play listings under the same setting', () => { expect(catalogueRouteVisible(state, '/slots/rainbow-realm/demo')).toBe(true) })
  it('does not list unregistered games', () => { expect(catalogueRouteVisible(state, '/games/unreviewed')).toBe(false) })
  it('can unhide a previously hidden game without deleting its route', () => { expect(catalogueRouteVisible({ ...state, games: state.games.map(game => ({ ...game, visible: true })) }, '/games/horse-flight')).toBe(true) })
})
