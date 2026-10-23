import { andarBaharSides, type AndarBaharSide } from './contracts'

export const chipValues = [.5, 1, 2, 5, 10, 25, 50] as const
export type ChipValue = typeof chipValues[number]
export type PendingWager = Readonly<{ key: string; side: AndarBaharSide; stake: number; chips: readonly ChipValue[] }>
export type HistoryItem = Readonly<{ id: string; winner: AndarBaharSide }>

export function readStorage(key: string): string | null { try { return localStorage.getItem(key) } catch { return null } }
export function writeStorage(key: string, value: string): boolean {
  try { localStorage.setItem(key, value); return localStorage.getItem(key) === value } catch { return false }
}
export function removeStorage(key: string) { try { localStorage.removeItem(key) } catch { /* The server still retains the settled round. */ } }
// Pending requests belong to one tab. sessionStorage survives reload without letting
// a second tab replace or delete this tab's unresolved idempotency key.
export function writePending(key: string, wager: PendingWager): boolean {
  try { const json = JSON.stringify(wager); sessionStorage.setItem(key, json); return sessionStorage.getItem(key) === json } catch { return false }
}
export function clearPending(key: string, requestKey?: string) {
  try {
    const current = readPending(key)
    if (!current || current.key === requestKey) sessionStorage.removeItem(key)
  } catch { /* The authoritative round remains recoverable from the server. */ }
}
export function sumChips(chips: readonly number[]) { return chips.reduce((total, value) => total + Math.round(value * 100), 0) / 100 }
export function readPending(key: string): PendingWager | null {
  try {
    const v: unknown = JSON.parse(sessionStorage.getItem(key) ?? 'null')
    if (!v || typeof v !== 'object') return null
    const p = v as Partial<PendingWager>
    if (typeof p.key !== 'string' || !/^andar-bahar-[a-zA-Z0-9-]{16,100}$/.test(p.key) || !andarBaharSides.includes(p.side!) || !Array.isArray(p.chips) || p.chips.length < 1 || p.chips.length > 200 || !p.chips.every(c => chipValues.includes(c)) || p.stake !== sumChips(p.chips) || p.stake < .5 || p.stake > 100) return null
    return p as PendingWager
  } catch { return null }
}
export function chipsFor(stake: number): ChipValue[] {
  let cents = Math.round(stake * 100)
  const chips: ChipValue[] = []
  for (const value of [...chipValues].reverse()) while (cents >= value * 100) { chips.push(value); cents -= value * 100 }
  return chips
}
export function readHistory(key: string): HistoryItem[] {
  try {
    const v: unknown = JSON.parse(readStorage(key) ?? '[]')
    return Array.isArray(v) ? v.filter((item): item is HistoryItem => !!item && typeof item.id === 'string' && andarBaharSides.includes(item.winner)).slice(-30) : []
  } catch { return [] }
}
