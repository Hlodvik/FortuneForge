import { useEffect } from 'react'
import { fetchWithAccountSession, getCurrentAccount } from '../account/services/accountsApi'
import { SLOT_ROUTE_DEFINITIONS } from '../../games/slots/routeRegistry'
import { isGameActivityApiEnabled } from './gameActivityCapability'

const cardGames = ['blackjack', 'texas-holdem', 'solitaire', 'three-card-poker', 'house-holdem', 'hearts']
const otherGames = ['dragon-tiger', 'mines', 'andar-bahar', 'hi-lo', 'baccarat', 'casino-war', 'video-poker', 'roulette', 'craps', 'keno', 'sic-bo', 'liars-dice', '2048', 'drop-merge']
const gameRoutes = new Map([
  ...cardGames.map(id => [`/cards/${id}`, id] as const),
  ...otherGames.map(id => [`/games/${id}`, id] as const),
  ...SLOT_ROUTE_DEFINITIONS.flatMap(game => game.playPath ? [[game.playPath, game.id] as const] : []),
])

export function presenceGameId(pathname: string): string | undefined {
  return gameRoutes.get(pathname.replace(/\/+$/, '') || '/')
}

/** Observed foreground time only. This never creates a completed play or a monetary event. */
export function useGamePresence(pathname: string) {
  const activityApiEnabled = isGameActivityApiEnabled()
  const gameId = presenceGameId(pathname)
  useEffect(() => {
    if (!activityApiEnabled || gameId === undefined) return undefined
    const sessionId = crypto.randomUUID()
    let disposed = false
    let authenticated = false
    let stopped = false
    let lastVisible: boolean | undefined
    let timer: ReturnType<typeof setInterval> | undefined
    let requests = Promise.resolve()

    const send = (visible: boolean) => {
      if (!authenticated || stopped) return
      // Serialize visibility transitions: a late foreground request cannot
      // reopen a lease after the tab has sent its hidden/cleanup signal.
      requests = requests.then(async () => {
        if (stopped) return
        try {
          const response = await fetchWithAccountSession('/api/games/activity/presence', {
            method: 'POST', credentials: 'same-origin', keepalive: true,
            headers: { 'Content-Type': 'application/json', 'X-FortuneForge-Presence': '1' },
            body: JSON.stringify({ gameId, visible, sessionId }),
          })
          if (response.status === 401 || response.status === 403) stopped = true
        } catch { /* Presence is best effort; the server lease expires after 90 seconds. */ }
      })
    }
    const stopTimer = () => { if (timer !== undefined) clearInterval(timer); timer = undefined }
    const update = (forceHidden = false) => {
      if (!authenticated || disposed) return
      const visible = !forceHidden && document.visibilityState === 'visible' && document.hasFocus()
      if (visible !== lastVisible) { send(visible); lastVisible = visible }
      stopTimer()
      if (visible) timer = setInterval(() => send(true), 25_000)
    }
    const updateVisibility = () => update()
    const hide = () => update(true)
    document.addEventListener('visibilitychange', updateVisibility)
    window.addEventListener('focus', updateVisibility)
    window.addEventListener('blur', hide)
    window.addEventListener('pagehide', hide)
    window.addEventListener('pageshow', updateVisibility)
    void getCurrentAccount().then(account => {
      if (disposed || !account.userId) return
      authenticated = true
      update()
    }).catch(() => undefined)

    return () => {
      disposed = true
      stopTimer()
      document.removeEventListener('visibilitychange', updateVisibility)
      window.removeEventListener('focus', updateVisibility)
      window.removeEventListener('blur', hide)
      window.removeEventListener('pagehide', hide)
      window.removeEventListener('pageshow', updateVisibility)
      if (lastVisible === true) send(false)
    }
  }, [gameId, activityApiEnabled])
}
