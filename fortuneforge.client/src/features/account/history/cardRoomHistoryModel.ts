// Edited: 2026-11-18T22:53:33-06:00 | Computer: Vinnabok
import type { CardRoomHistoryResult } from './cardRoomHistoryApi'
import type { CardRoomActivity } from './cardRoomHistoryTypes'
import type { ActiveCardRoomGame } from '../services/cardRoomActivityApi'

/** Only the account summary fields are accepted into the activity view model. */
export function projectCardRoomSession(game: ActiveCardRoomGame, session: unknown): CardRoomActivity | null {
  if (!isRecord(session)) throw invalidSession(game)
  if (session.kind === 'idle' || session.kind === 'result' && (game === 'texas-holdem' || game === 'solitaire')) return null
  const queued = session.kind === (game === 'solitaire' ? 'queued' : 'queue')
  if (queued) {
    if (typeof session.ticketId !== 'string' || !isDate(session.joinedAtUtc) || !Array.isArray(session.players)
      || game === 'solitaire' && !isCount(session.playerCount)) throw invalidSession(game)
    const count = session.players.length
    return activeActivity(game, session.ticketId, session.joinedAtUtc,
      game === 'solitaire' ? 'Waiting for a game' : 'Waiting for a table',
      game === 'solitaire' ? `${count} of ${session.playerCount} seats`
        : game === 'three-card-poker' ? `${count} players ready` : `${count} player${count === 1 ? '' : 's'} ready`)
  }
  if (game === 'solitaire') {
    if (session.kind !== 'match' || typeof session.matchId !== 'string' || !isDate(session.startedAtUtc)
      || !isCount(session.score) || !isCount(session.moves)) throw invalidSession(game)
    return activeActivity(game, session.matchId, session.startedAtUtc, 'Solitaire run',
      `${session.score.toLocaleString()} points · ${session.moves} moves`)
  }
  if (session.kind !== (game === 'texas-holdem' ? 'match' : 'table') || !isRecord(session.table)) throw invalidSession(game)
  const table = session.table
  if (game === 'texas-holdem') {
    if (typeof table.matchId !== 'string' || !isDate(table.startedAtUtc) || !isCount(table.handNumber)
      || !isCount(table.pot) || !isCount(table.currentBet)) throw invalidSession(game)
    return activeActivity(game, table.matchId, table.startedAtUtc, `Table · hand ${table.handNumber}`,
      `Pot R${(table.pot / 100).toFixed(2)} · current bet R${(table.currentBet / 100).toFixed(2)}`)
  }
  if (typeof table.tableId !== 'string' || !isDate(table.createdAtUtc) || !isCount(table.round)
    || typeof table.phase !== 'string') throw invalidSession(game)
  return activeActivity(game, table.tableId, table.createdAtUtc, `Table · round ${table.round}`,
    table.phase.replaceAll('-', ' ').replace(/\b\w/g, character => character.toUpperCase()))
}

function activeActivity(game: ActiveCardRoomGame, id: string, startedAtUtc: string, title: string, summary: string): CardRoomActivity {
  return { id, matchId: id, game, gameLabel: gameLabel(game), title, summary, startedAtUtc,
    completedAtUtc: null, unseen: false, requiresClaim: false, winningsCredits: null,
    sourceIds: [], rounds: 0, wagerCredits: 0, netCredits: 0 }
}

function invalidSession(game: ActiveCardRoomGame) { return new Error(`The server returned an invalid ${gameLabel(game)} activity.`) }
function isRecord(value: unknown): value is Record<string, unknown> { return typeof value === 'object' && value !== null && !Array.isArray(value) }
function isDate(value: unknown): value is string { return typeof value === 'string' && Number.isFinite(Date.parse(value)) }
function isCount(value: unknown): value is number { return typeof value === 'number' && Number.isSafeInteger(value) && value >= 0 }

export function historyActivity(result: CardRoomHistoryResult): CardRoomActivity {
  const label = gameLabel(result.game)
  const detail = result.game === 'mines'
    ? result.winningsCredits > 0 ? `R${result.winningsCredits.toFixed(2)} returned` : 'No return'
    : result.game === 'solitaire' && result.score !== null
    ? `${result.score.toLocaleString()} points${result.moves === null ? '' : ` · ${result.moves} moves`}`
    : result.winningsCredits > 0
      ? `R${result.winningsCredits.toFixed(2)} won`
      : 'Result ready'
  return {
    id: result.resultId,
    matchId: result.matchId,
    game: result.game,
    gameLabel: label,
    title: result.unseen ? `${label} result` : `${label} game`,
    summary: detail,
    startedAtUtc: result.startedAtUtc,
    completedAtUtc: result.completedAtUtc,
    unseen: result.unseen,
    requiresClaim: result.requiresClaim,
    winningsCredits: result.winningsCredits,
    sourceIds: [result.resultId],
    rounds: 1,
    wagerCredits: result.wagerCredits,
    netCredits: result.netCredits,
  }
}

export function groupHistoryActivities(items: readonly CardRoomActivity[]): CardRoomActivity[] {
  const grouped = new Map<string, CardRoomActivity[]>()
  for (const item of items) {
    const key = item.game === 'solitaire' ? `${item.game}:${item.id}` : `${item.game}:${item.matchId}`
    grouped.set(key, [...(grouped.get(key) ?? []), item])
  }
  return [...grouped.values()].map((group) => {
    const ordered = [...group].sort((left, right) => Date.parse(left.startedAtUtc) - Date.parse(right.startedAtUtc))
    const first = ordered[0]
    const last = ordered[ordered.length - 1]
    if (first.game === 'solitaire' || first.game === 'house-holdem' || first.game === 'hi-lo' || first.game === 'andar-bahar' || first.game === 'mines') return first
    const rounds = ordered.reduce((sum, item) => sum + (item.rounds ?? 1), 0)
    const label = first.game === 'texas-holdem' ? 'hands' : 'rounds'
    const wagerCredits = ordered.reduce((sum, item) => sum + (item.wagerCredits ?? 0), 0)
    const winningsCredits = ordered.reduce((sum, item) => sum + (item.winningsCredits ?? 0), 0)
    const netCredits = ordered.reduce((sum, item) => sum + (item.netCredits ?? 0), 0)
    return {
      ...last,
      id: `${first.game}:${first.matchId}`,
      title: `${rounds} ${label} at one table`,
      summary: `Net ${money(netCredits)} · ${elapsed(first.startedAtUtc, last.completedAtUtc)}`,
      startedAtUtc: first.startedAtUtc,
      completedAtUtc: last.completedAtUtc,
      unseen: ordered.some((item) => item.unseen),
      requiresClaim: false,
      winningsCredits,
      sourceIds: ordered.flatMap((item) => item.sourceIds ?? [item.id]),
      rounds,
      wagerCredits,
      netCredits,
    }
  })
}

function elapsed(startedAtUtc: string, completedAtUtc: string | null): string {
  if (completedAtUtc === null) return 'In progress'
  const minutes = Math.max(1, Math.round((Date.parse(completedAtUtc) - Date.parse(startedAtUtc)) / 60_000))
  return `${minutes} min at table`
}

function money(value: number): string {
  return `${value < 0 ? '−' : ''}R${Math.abs(value).toFixed(2)}`
}

function gameLabel(game: CardRoomActivity['game']): string {
  if (game === 'blackjack') return 'Blackjack'
  if (game === 'texas-holdem') return 'Texas Hold’em'
  if (game === 'three-card-poker') return 'Three Card Poker'
  if (game === 'house-holdem') return 'House Hold’em'
  if (game === 'hi-lo') return 'Hi-Lo'
  if (game === 'andar-bahar') return 'Andar Bahar'
  if (game === 'mines') return 'Mines'
  return 'Solitaire'
}

