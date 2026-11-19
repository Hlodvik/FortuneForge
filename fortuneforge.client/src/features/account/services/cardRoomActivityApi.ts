// Edited: 2026-11-18T22:56:39-06:00 | Computer: Vinnabok
import { fetchWithAccountSession } from './accountsApi'

export type ActiveCardRoomGame = 'blackjack' | 'texas-holdem' | 'three-card-poker' | 'solitaire'

const sessionPaths: Readonly<Record<ActiveCardRoomGame, string>> = {
  blackjack: '/api/cards/blackjack/table/session',
  'texas-holdem': '/api/cards/texas-holdem/credit/session',
  'three-card-poker': '/api/cards/three-card-poker/table/session',
  solitaire: '/api/solitaire/session',
}

/** Consumers validate their own projection of the authenticated session response. */
export function requestCardRoomSession(game: ActiveCardRoomGame, signal?: AbortSignal): Promise<Response> {
  return fetchWithAccountSession(sessionPaths[game], { method: 'GET', cache: 'no-store', signal })
}

export function requestSolitaireResultClaim(matchId: string, idempotencyKey: string): Promise<Response> {
  return fetchWithAccountSession(`/api/solitaire/matches/${encodeURIComponent(matchId)}/claim`, {
    method: 'POST', headers: new Headers({ 'Content-Type': 'application/json', 'Idempotency-Key': idempotencyKey }),
  })
}
