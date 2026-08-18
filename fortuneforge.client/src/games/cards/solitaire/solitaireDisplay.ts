export function formatCountdown(deadlineAtUtc: string, now: number): string {
  const remaining = Math.max(0, Date.parse(deadlineAtUtc) - now)
  return formatDuration(Math.ceil(remaining / 1_000))
}

export function formatDuration(seconds: number): string {
  const minutes = Math.floor(seconds / 60)
  return `${minutes}:${String(seconds % 60).padStart(2, '0')}`
}

export function formatSignedCredits(value: number): string {
  return `${value >= 0 ? '+' : '−'}R${Math.abs(value).toFixed(2)}`
}
