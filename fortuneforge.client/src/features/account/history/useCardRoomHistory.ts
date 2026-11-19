// Edited: 2026-11-18T22:53:33-06:00 | Computer: Vinnabok
import { groupHistoryActivities, historyActivity } from './cardRoomHistoryModel'
import { useCallback, useEffect, useState } from 'react'
import {
  getCardRoomHistory,
  markCardRoomResultSeen,
  getActiveCardRoomActivity,
  claimSolitaireHistoryResult,
} from './cardRoomHistoryApi'
import type { CardRoomActivity } from './cardRoomHistoryTypes'

const gameHref: Record<CardRoomActivity['game'], string> = {
  blackjack: '/cards/blackjack',
  'texas-holdem': '/cards/texas-holdem',
  'three-card-poker': '/cards/three-card-poker',
  'house-holdem': '/cards/house-holdem',
  solitaire: '/cards/solitaire',
  'hi-lo': '/games/hi-lo',
  'andar-bahar': '/games/andar-bahar',
  mines: '/games/mines',
}

export function useCardRoomHistory(onBalanceChange?: (balanceCredits: number) => void) {
  const [activities, setActivities] = useState<readonly CardRoomActivity[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [busyId, setBusyId] = useState<string | null>(null)

  const refresh = useCallback(async (signal?: AbortSignal) => {
    const [historyResult, blackjackResult, holdemResult, solitaireResult, threeCardPokerResult] = await Promise.allSettled([
      getCardRoomHistory(40, signal),
      getActiveCardRoomActivity('blackjack', signal),
      getActiveCardRoomActivity('texas-holdem', signal),
      getActiveCardRoomActivity('solitaire', signal),
      getActiveCardRoomActivity('three-card-poker', signal),
    ])
    if (signal?.aborted) return

    const completed = historyResult.status === 'fulfilled'
      ? groupHistoryActivities(historyResult.value.map(historyActivity))
      : []
    const active = [
      blackjackResult.status === 'fulfilled' ? blackjackResult.value : null,
      holdemResult.status === 'fulfilled' ? holdemResult.value : null,
      solitaireResult.status === 'fulfilled' ? solitaireResult.value : null,
      threeCardPokerResult.status === 'fulfilled' ? threeCardPokerResult.value : null,
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
        const result = await claimSolitaireHistoryResult(
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
