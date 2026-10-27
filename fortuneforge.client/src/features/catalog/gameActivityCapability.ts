/** Enable only after the recent-games, popularity and presence APIs are deployed. */
export function isGameActivityApiEnabled(): boolean {
  return import.meta.env.VITE_GAME_ACTIVITY_API_ENABLED === 'true'
}
