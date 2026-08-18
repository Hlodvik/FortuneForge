import { useCallback, useEffect, useState } from 'react'
import {
  getCardRoomHistory,
  markCardRoomResultSeen,
  type CardRoomHistoryResult,
} from '../../games/cards/shared/cardRoomHistoryApi'
import type { CardRoomActivity } from '../../games/cards/shared/cardRoomHistoryTypes'

const gameHref: Record<CardRoomActivity['game'], string> = {
  blackjack: '/cards/blackjack',
  'texas-holdem': '/cards/texas-holdem',
  solitaire: '/cards/solitaire',
}

export function useCardRoomHistory(onBalanceChange?: (balanceCredits: number) => void) {
  const [activities, setActivities] = useState<readonly CardRoomActivity[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [busyId, setBusyId] = useState<string | null>(null)

  const refresh = useCallback(async (signal?: AbortSignal) => {
    const [historyResult, blackjackResult, holdemResult, solitaireResult] = await Promise.allSettled([
      getCardRoomHistory(40, signal),
      import('../../games/cards/blackjack/blackjackTableApi')
        .then(({ getBlackjackTableSession }) => getBlackjackTableSession(signal)),
      import('../../games/cards/texasHoldem/creditHoldemApi')
        .then(({ getCreditHoldemSession }) => getCreditHoldemSession(signal)),
      import('../../games/cards/solitaire/solitaireApi')
        .then(({ getSolitaireSession }) => getSolitaireSession(signal)),
    ])
    if (signal?.aborted) return

    const completed = historyResult.status === 'fulfilled'
      ? groupHistoryActivities(historyResult.value.map(historyActivity))
      : []
    const active = [
      blackjackResult.status === 'fulfilled' ? blackjackActivity(blackjackResult.value) : null,
      holdemResult.status === 'fulfilled' ? holdemActivity(holdemResult.value) : null,
      solitaireResult.status === 'fulfilled' ? solitaireActivity(solitaireResult.value) : null,
    ].filter((activity): activity is CardRoomActivity => activity !== null)

    const activeKeys = new Set(active.map((activity) => `${activity.game}:${activity.matchId}`))
    setActivities([
      ...active,
      ...completed.filter((activity) => !activeKeys.has(`${activity.game}:${activity.matchId}`)),
    ].sort((left, right) => Date.parse(right.completedAtUtc ?? right.startedAtUtc)
      - Date.parse(left.completedAtUtc ?? left.startedAtUtc)))
    setError(historyResult.status === 'rejected'
      ? 'Completed games could not be loaded. Active games are still available.'
      : null)
    setLoading(false)
  }, [])

  useEffect(() => {
    const controller = new AbortController()
    void refresh(controller.signal)
    const poll = window.setInterval(() => {
      if (document.visibilityState === 'visible') void refresh(controller.signal)
    }, 30_000)
    return () => {
      controller.abort()
      window.clearInterval(poll)
    }
  }, [refresh])

  const select = useCallback(async (activity: CardRoomActivity) => {
    if (activity.completedAtUtc === null) {
      window.location.assign(gameHref[activity.game])
      return
    }

    setBusyId(activity.id)
    setError(null)
    try {
      if (activity.requiresClaim && activity.game === 'solitaire') {
        const { claimSolitaireResult } = await import('../../games/cards/solitaire/solitaireApi')
        const result = await claimSolitaireResult(
          activity.matchId,
          `history_${crypto.randomUUID().replaceAll('-', '')}`,
        )
        onBalanceChange?.(result.balanceCredits)
      } else if (activity.unseen) {
        await Promise.all((activity.sourceIds ?? [activity.id]).map((resultId) => markCardRoomResultSeen(resultId)))
      }
      setActivities((current) => current.map((item) => item.id === activity.id
        ? { ...item, unseen: false, requiresClaim: false }
        : item))
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : 'The game result could not be opened.')
    } finally {
      setBusyId(null)
    }
  }, [onBalanceChange])

  return { activities, loading, error, busyId, refresh, select }
}

function historyActivity(result: CardRoomHistoryResult): CardRoomActivity {
  const label = gameLabel(result.game)
  const detail = result.game === 'solitaire' && result.score !== null
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

function blackjackActivity(session: Awaited<ReturnType<
  typeof import('../../games/cards/blackjack/blackjackTableApi').getBlackjackTableSession
>>): CardRoomActivity | null {
  if (session.kind === 'idle') return null
  if (session.kind === 'queue') return {
    id: session.ticketId,
    matchId: session.ticketId,
    game: 'blackjack',
    gameLabel: 'Blackjack',
    title: 'Waiting for a table',
    summary: `${session.players.length} player${session.players.length === 1 ? '' : 's'} ready`,
    startedAtUtc: session.joinedAtUtc,
    completedAtUtc: null,
    unseen: false,
    requiresClaim: false,
    winningsCredits: null,
    sourceIds: [], rounds: 0, wagerCredits: 0, netCredits: 0,
  }
  return {
    id: session.table.tableId,
    matchId: session.table.tableId,
    game: 'blackjack',
    gameLabel: 'Blackjack',
    title: `Table · round ${session.table.round}`,
    summary: readable(session.table.phase),
    startedAtUtc: session.table.createdAtUtc,
    completedAtUtc: null,
    unseen: false,
    requiresClaim: false,
    winningsCredits: null,
    sourceIds: [], rounds: 0, wagerCredits: 0, netCredits: 0,
  }
}

function holdemActivity(session: Awaited<ReturnType<
  typeof import('../../games/cards/texasHoldem/creditHoldemApi').getCreditHoldemSession
>>): CardRoomActivity | null {
  if (session.kind === 'idle' || session.kind === 'result') return null
  if (session.kind === 'queue') return {
    id: session.ticketId,
    matchId: session.ticketId,
    game: 'texas-holdem',
    gameLabel: 'Hold’em',
    title: 'Waiting for a table',
    summary: `${session.players.length} player${session.players.length === 1 ? '' : 's'} ready`,
    startedAtUtc: session.joinedAtUtc,
    completedAtUtc: null,
    unseen: false,
    requiresClaim: false,
    winningsCredits: null,
    sourceIds: [], rounds: 0, wagerCredits: 0, netCredits: 0,
  }
  return {
    id: session.table.matchId,
    matchId: session.table.matchId,
    game: 'texas-holdem',
    gameLabel: 'Hold’em',
    title: `Table · hand ${session.table.handNumber}`,
    summary: `Pot R${(session.table.pot / 100).toFixed(2)} · current bet R${(session.table.currentBet / 100).toFixed(2)}`,
    startedAtUtc: session.table.startedAtUtc,
    completedAtUtc: null,
    unseen: false,
    requiresClaim: false,
    winningsCredits: null,
    sourceIds: [], rounds: 0, wagerCredits: 0, netCredits: 0,
  }
}

function solitaireActivity(session: Awaited<ReturnType<
  typeof import('../../games/cards/solitaire/solitaireApi').getSolitaireSession
>>): CardRoomActivity | null {
  if (session.kind === 'idle' || session.kind === 'result') return null
  if (session.kind === 'queued') return {
    id: session.ticketId,
    matchId: session.ticketId,
    game: 'solitaire',
    gameLabel: 'Solitaire',
    title: 'Waiting for a game',
    summary: `${session.players.length} of ${session.playerCount} seats`,
    startedAtUtc: session.joinedAtUtc,
    completedAtUtc: null,
    unseen: false,
    requiresClaim: false,
    winningsCredits: null,
    sourceIds: [], rounds: 0, wagerCredits: 0, netCredits: 0,
  }
  return {
    id: session.matchId,
    matchId: session.matchId,
    game: 'solitaire',
    gameLabel: 'Solitaire',
    title: 'Solitaire run',
    summary: `${session.score.toLocaleString()} points · ${session.moves} moves`,
    startedAtUtc: session.startedAtUtc,
    completedAtUtc: null,
    unseen: false,
    requiresClaim: false,
    winningsCredits: null,
    sourceIds: [], rounds: 0, wagerCredits: 0, netCredits: 0,
  }
}

function groupHistoryActivities(items: readonly CardRoomActivity[]): CardRoomActivity[] {
  const grouped = new Map<string, CardRoomActivity[]>()
  for (const item of items) {
    const key = item.game === 'solitaire' ? `${item.game}:${item.id}` : `${item.game}:${item.matchId}`
    grouped.set(key, [...(grouped.get(key) ?? []), item])
  }
  return [...grouped.values()].map((group) => {
    const ordered = [...group].sort((left, right) => Date.parse(left.startedAtUtc) - Date.parse(right.startedAtUtc))
    const first = ordered[0]
    const last = ordered[ordered.length - 1]
    if (first.game === 'solitaire') return first
    const rounds = ordered.reduce((sum, item) => sum + (item.rounds ?? 1), 0)
    const label = first.game === 'blackjack' ? 'rounds' : 'hands'
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
  if (game === 'texas-holdem') return 'Hold’em'
  return 'Solitaire'
}

function readable(value: string): string {
  return value.replaceAll('-', ' ').replace(/\b\w/g, (character) => character.toUpperCase())
}
