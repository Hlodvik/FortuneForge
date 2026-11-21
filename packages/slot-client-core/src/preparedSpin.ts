export type PreparedSlotSpin = {
  operationId: string
  wagerPoints: number
  useFreeSpin: boolean
  useSpecialBoost: boolean
}

// An older gateway has no route here, so a keyed retry cannot become a new spin.
export const preparedSlotSpinPath = '/api/slots/prepared-spins'

export function preparedSlotSpinHeaders(accountId?: string, operationId?: string): Record<string, string> | undefined {
  if (!accountId) return undefined
  const headers: Record<string, string> = { 'X-FortuneForge-Expected-Account': accountId }
  if (operationId) headers['Idempotency-Key'] = operationId
  return headers
}

export function isPreparedSlotSpin(value: unknown): value is PreparedSlotSpin {
  if (value === null || typeof value !== 'object') return false
  const spin = value as Partial<PreparedSlotSpin>
  return typeof spin.operationId === 'string' && /^[A-Za-z0-9_-]{16,128}$/.test(spin.operationId) &&
    typeof spin.wagerPoints === 'number' && Number.isSafeInteger(spin.wagerPoints) && spin.wagerPoints > 0 &&
    typeof spin.useFreeSpin === 'boolean' && typeof spin.useSpecialBoost === 'boolean'
}

/** Retains the exact operation for a player and game if a paid reply was lost. */
export function createPreparedSlotSpinStore(gameStorageName: string) {
  const inMemory = new Map<string, PreparedSlotSpin>()
  const key = (accountId: string) => `fortuneforge:${gameStorageName}-spin:${accountId}`

  function read(accountId: string): PreparedSlotSpin | undefined {
    const cached = inMemory.get(accountId)
    if (cached) return cached
    try {
      const stored: unknown = JSON.parse(sessionStorage.getItem(key(accountId)) ?? 'null')
      return isPreparedSlotSpin(stored) ? stored : undefined
    } catch { return undefined }
  }

  function remember(accountId: string, spin: PreparedSlotSpin) {
    inMemory.set(accountId, spin)
    try { sessionStorage.setItem(key(accountId), JSON.stringify(spin)) } catch { /* Keep the in-memory attempt. */ }
  }

  function forget(accountId: string, operationId: string) {
    if (read(accountId)?.operationId !== operationId) return
    inMemory.delete(accountId)
    try { sessionStorage.removeItem(key(accountId)) } catch { /* Storage can be disabled. */ }
  }

  return { read, remember, forget }
}
