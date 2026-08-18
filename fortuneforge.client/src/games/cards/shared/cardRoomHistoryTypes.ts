export type CardRoomActivity = Readonly<{
  id: string
  matchId: string
  game: 'blackjack' | 'texas-holdem' | 'solitaire'
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
