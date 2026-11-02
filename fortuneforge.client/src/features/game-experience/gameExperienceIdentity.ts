import { GAME_LOADING_PRESENTATIONS, resolveGameLoadingEntry } from '../game-loading/gameLoadingRegistry'

// These playable routes deliberately have no catalogue/loading artwork entry.
const unlisted = new Map([
  ['/games/asteroids', 'asteroids'], ['/games/flappy', 'flappy'],
  ['/games/snake', 'snake'], ['/games/horse-flight', 'horse-flight'],
])
export const CANONICAL_EXPERIENCE_GAME_IDS = Object.freeze([
  ...GAME_LOADING_PRESENTATIONS.map(entry => entry.id), ...unlisted.values(),
].sort())

export function gameExperienceIdentity(pathname: string) {
  const path = pathname.split(/[?#]/, 1)[0].replace(/\/+$/, '') || '/'
  return resolveGameLoadingEntry(path)?.id ?? unlisted.get(path) ?? null
}
export function isDemoExperienceRoute(pathname: string) {
  const path = pathname.split(/[?#]/, 1)[0].replace(/\/+$/, '')
  return path.startsWith('/demo/') || path.endsWith('/demo')
}
