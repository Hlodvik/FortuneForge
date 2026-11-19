// Edited: 2026-11-18T22:53:33-06:00 | Computer: Vinnabok
export type CardRoomGame = 'blackjack' | 'texas-holdem' | 'three-card-poker' | 'house-holdem' | 'solitaire' | 'hi-lo' | 'andar-bahar' | 'mines'

export type CardRoomActivity = Readonly<{
  id: string
  matchId: string
  game: CardRoomGame
  gameLabel: string
  title: string
  summary: string
  startedAtUtc: string
  completedAtUtc: string | null
  unseen: boolean
  requiresClaim: boolean
  winningsCredits: number | null
  sourceIds?: readonly string[]
  rounds?: number
  wagerCredits?: number
  netCredits?: number
}>

export function cardRoomUnseenCount(activities: readonly CardRoomActivity[]): number {
  return activities.filter((activity) => activity.completedAtUtc !== null && activity.unseen).length
}
