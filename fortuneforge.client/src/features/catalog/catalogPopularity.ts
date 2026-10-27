import { useCallback, useEffect, useState } from 'react'
import { isGameActivityApiEnabled } from './gameActivityCapability'

export type CataloguePopularityGame = { id: string; route: string; playCount: number | null }
type CataloguePopularity = {
  metric: 'recorded-plays'
  trackingStartedAtUtc: string | null
  games: CataloguePopularityGame[]
}

const unavailable = 'Game popularity is temporarily unavailable.'

export function parseCataloguePopularity(value: unknown): CataloguePopularity {
  if (!value || typeof value !== 'object') throw new Error(unavailable)
  const data = value as Record<string, unknown>
  const started = data.trackingStartedAtUtc
  if (data.metric !== 'recorded-plays' || !Array.isArray(data.games) ||
      !(started === null || (typeof started === 'string' && Number.isFinite(Date.parse(started))))) throw new Error(unavailable)
  const ids = new Set<string>()
  const routes = new Set<string>()
  const games = data.games.map((item: unknown): CataloguePopularityGame => {
    if (!item || typeof item !== 'object') throw new Error(unavailable)
    const { id, route, playCount } = item as Record<string, unknown>
    if (typeof id !== 'string' || !/^[a-z0-9-]+$/.test(id) || ids.has(id) ||
        typeof route !== 'string' || !/^\/(cards|games|slots)\/[a-z0-9-]+$/.test(route) || routes.has(route) ||
        !(playCount === null || (typeof playCount === 'number' && Number.isSafeInteger(playCount) && playCount >= 0)) ||
        (started === null && playCount !== null)) throw new Error(unavailable)
    ids.add(id); routes.add(route)
    return { id, route, playCount }
  })
  return { metric: 'recorded-plays', trackingStartedAtUtc: started as string | null, games }
}

/** Unknown counts stay unknown; stable alphabetical ties are not popularity claims. */
export function orderByCataloguePopularity<T extends { href: string; title: string }>(
  games: readonly T[], popularity: readonly CataloguePopularityGame[] | null,
): T[] {
  const counts = new Map(popularity?.map(game => [game.route, game.playCount]))
  const compareText = (left: string, right: string) => left < right ? -1 : left > right ? 1 : 0
  return [...games].sort((left, right) => {
    const leftCount = counts.get(left.href) ?? -1
    const rightCount = counts.get(right.href) ?? -1
    return rightCount - leftCount || compareText(left.title.toLowerCase(), right.title.toLowerCase()) || compareText(left.href, right.href)
  })
}

/** Server-recorded plays across accounts, refreshed without client-side increments. */
export function useCataloguePopularity() {
  const activityApiEnabled = isGameActivityApiEnabled()
  const [snapshot, setSnapshot] = useState<CataloguePopularity | null>(null)
  const [isLoading, setIsLoading] = useState(activityApiEnabled)
  const [error, setError] = useState('')
  const [revision, setRevision] = useState(0)
  const reload = useCallback(() => setRevision(value => value + 1), [])
  useEffect(() => {
    if (!activityApiEnabled) {
      setSnapshot(null); setIsLoading(false); setError('')
      return undefined
    }
    const abort = new AbortController()
    setIsLoading(true)
    void fetch('/api/catalog/popularity', {
      cache: 'no-store', headers: { Accept: 'application/json' }, signal: abort.signal,
    }).then(async response => {
      if (!response.ok) throw new Error(unavailable)
      return parseCataloguePopularity(await response.json())
    }).then(next => {
      if (!abort.signal.aborted) { setSnapshot(next); setError('') }
    }).catch(() => {
      if (!abort.signal.aborted) setError(unavailable)
    }).finally(() => {
      if (!abort.signal.aborted) setIsLoading(false)
    })
    return () => abort.abort()
  }, [revision, activityApiEnabled])
  useEffect(() => {
    if (!activityApiEnabled) return undefined
    const refresh = () => { if (document.visibilityState !== 'hidden') reload() }
    const timer = window.setInterval(refresh, 60_000)
    window.addEventListener('focus', refresh)
    return () => { window.clearInterval(timer); window.removeEventListener('focus', refresh) }
  }, [reload, activityApiEnabled])
  return { games: snapshot?.games ?? null, trackingStartedAtUtc: snapshot?.trackingStartedAtUtc ?? null, isLoading, error, reload }
}
