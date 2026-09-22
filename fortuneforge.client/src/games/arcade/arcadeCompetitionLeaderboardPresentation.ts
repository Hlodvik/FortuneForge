import type {
  ArcadeCompetitionPeriod,
  ArcadeCompetitionWindow,
} from './arcadeCompetitionApi'

export type CompetitionHistoryDirection = 'previous' | 'next'

export function competitionHistoryAt(
  window: ArcadeCompetitionWindow,
  direction: CompetitionHistoryDirection,
): string {
  const boundary = direction === 'previous'
    ? Date.parse(window.startsAtUtc) - 1
    : Date.parse(window.endsAtUtc)
  if (!Number.isFinite(boundary)) {
    throw new Error('The competition window has an invalid date.')
  }
  return new Date(boundary).toISOString()
}

/** Period tabs always return to the current window instead of retaining a prior at value. */
export function selectCompetitionPeriod(period: ArcadeCompetitionPeriod): Readonly<{
  period: ArcadeCompetitionPeriod
  at: undefined
}> {
  return { period, at: undefined }
}

export function periodLabel(period: ArcadeCompetitionPeriod): string {
  return period === 'all-time' ? 'All-time' : `${period[0].toUpperCase()}${period.slice(1)}`
}

export function formatCompetitionDateTime(value: string): string {
  const date = new Date(value)
  if (!Number.isFinite(date.getTime())) return 'Date unavailable'

  const parts = new Intl.DateTimeFormat('en-ZA', {
    timeZone: 'Africa/Johannesburg',
    day: '2-digit',
    month: 'short',
    year: 'numeric',
    hour: '2-digit',
    minute: '2-digit',
    hourCycle: 'h23',
  }).formatToParts(date)
  const valueFor = (type: Intl.DateTimeFormatPartTypes) => parts.find((part) => part.type === type)?.value
  return `${valueFor('day')} ${valueFor('month')} ${valueFor('year')}, ${valueFor('hour')}:${valueFor('minute')} SAST`
}

export function canNavigateToNextCompetitionWindow(window: ArcadeCompetitionWindow): boolean {
  return !window.entriesOpen
}

export function formatCompetitionCents(cents: number): string {
  return `R${(cents / 100).toFixed(2)}`
}
