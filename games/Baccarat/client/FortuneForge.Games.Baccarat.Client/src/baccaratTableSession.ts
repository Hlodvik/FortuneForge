const tableKey = (playerId: string | undefined) => `fortuneforge:baccarat:table:${playerId ?? 'guest'}`

export function readReloadedBaccaratTable(playerId: string | undefined): unknown {
  try {
    const navigation = performance.getEntriesByType('navigation')[0] as PerformanceNavigationTiming | undefined
    // A restored tab or a return from another page starts a new table. A Home
    // reload followed by client navigation must not revive an earlier visit.
    if (navigation?.type !== 'reload' || navigation.name && new URL(navigation.name).pathname !== location.pathname) return null
    return JSON.parse(sessionStorage.getItem(tableKey(playerId)) ?? 'null')
  } catch { return null }
}

export function storeBaccaratTable(playerId: string | undefined, table: unknown) {
  try { sessionStorage.setItem(tableKey(playerId), JSON.stringify(table)) } catch { /* The open table still works without storage. */ }
}

export function clearBaccaratTable(playerId: string | undefined) {
  try { sessionStorage.removeItem(tableKey(playerId)) } catch { /* Storage can be unavailable. */ }
}
