export type AccountRestrictionNotice = Readonly<{
  code: 'account_restricted' | 'game_restricted' | 'withdrawals_restricted'
  message: string
  gameId?: string
}>

let currentNotice: AccountRestrictionNotice | null = null
const listeners = new Set<() => void>()
export const getAccountRestriction = () => currentNotice
export const subscribeAccountRestriction = (listener: () => void) => {
  listeners.add(listener)
  return () => { listeners.delete(listener) }
}
export function clearAccountRestriction() {
  currentNotice = null
  listeners.forEach(listener => listener())
}
export function publishAccountRestriction(notice: AccountRestrictionNotice) {
  // A narrower restriction must never replace an account-wide lock.
  if (currentNotice?.code === 'account_restricted' && notice.code !== 'account_restricted') return
  currentNotice = notice
  listeners.forEach(listener => listener())
}

export async function observeRestrictionResponse(response: Response): Promise<Response> {
  if (response.status !== 403) return response
  try {
    const body: unknown = await response.clone().json()
    if (typeof body !== 'object' || body === null || !('code' in body)) return response
    if (body.code !== 'account_restricted' && body.code !== 'game_restricted' && body.code !== 'withdrawals_restricted') return response
    const fallback = body.code === 'account_restricted' ? 'Your account is under review.'
      : body.code === 'game_restricted' ? 'This game is unavailable for your account while it is under review.'
        : 'Withdrawals are temporarily unavailable while your account is under review.'
    publishAccountRestriction({ code: body.code,
      message: 'error' in body && typeof body.error === 'string' ? body.error : fallback,
      gameId: 'gameId' in body && typeof body.gameId === 'string' ? body.gameId : undefined,
    })
  } catch { /* An unrelated or malformed error still belongs to the original caller. */ }
  return response
}
