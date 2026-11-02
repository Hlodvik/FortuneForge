import { useEffect, useMemo, useSyncExternalStore, type ReactNode } from 'react'
import { GameExperienceProvider, type GameExperience } from '@fortuneforge/client-presentation/game-experience'
import { getExperienceSnapshot, getServerExperienceSnapshot, loadGameExperiences, refreshGameExperiences, subscribeGameExperiences } from './gameExperienceStore'
import { gameExperienceIdentity, isDemoExperienceRoute } from './gameExperienceIdentity'

const noopSubscribe = () => () => {}

/** Starts one shared data read alongside route loading, without changing game UI. */
export function GameExperienceRouteProvider({ pathname, fallbackGameId, children }: { pathname?: string; fallbackGameId?: string; children: ReactNode }) {
  const gameId = pathname ? gameExperienceIdentity(pathname) : fallbackGameId ?? null
  const demo = pathname !== undefined && isDemoExperienceRoute(pathname)
  const connected = pathname !== undefined && gameId !== null && !demo
  const snapshot = useSyncExternalStore(connected ? subscribeGameExperiences : noopSubscribe, getExperienceSnapshot, getServerExperienceSnapshot)
  useEffect(() => { if (connected) void loadGameExperiences() }, [connected])
  const value = useMemo<GameExperience>(() => {
    const fields = { gameId, userId: null, provenance: [] as readonly string[], coverage: 'unavailable' as const,
      firstPlayedAtUtc: null, error: null, refresh: connected ? refreshGameExperiences : () => {} }
    if (!gameId || !pathname) return { ...fields, status: 'unavailable', hasPlayed: null }
    if (demo || snapshot.status === 'guest') return { ...fields, status: 'guest', hasPlayed: null }
    if (snapshot.status === 'loading') return { ...fields, status: 'loading', hasPlayed: null }
    if (snapshot.status === 'error') return { ...fields, status: 'error', hasPlayed: null, error: snapshot.error }
    const row = snapshot.data?.games.find(item => item.gameId === gameId)
    if (!row) return { ...fields, status: 'unknown', hasPlayed: null }
    const known = { ...fields, userId: snapshot.data!.userId, provenance: row.provenance, coverage: row.coverage, firstPlayedAtUtc: row.firstPlayedAtUtc }
    return row.state === 'known' && row.hasPlayed !== null
      ? { ...known, status: 'known', hasPlayed: row.hasPlayed }
      : { ...known, status: 'unknown', hasPlayed: null }
  }, [connected, demo, gameId, pathname, snapshot])
  return <GameExperienceProvider value={value}>{children}</GameExperienceProvider>
}
