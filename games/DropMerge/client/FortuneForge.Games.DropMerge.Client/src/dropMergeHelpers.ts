export function tileClass(value: number): string {
  const rank = Math.max(0, Math.round(Math.log2(value)) - 1)
  return `tile-rank-${rank % 40}`
}

export function tileLabel(value: number): string {
  const units = [[1000000000000, 'T'], [1000000000, 'B'], [1000000, 'M'], [1000, 'K']] as const
  for (const [divisor, suffix] of units) {
    if (value < divisor) continue
    const scaled = value / divisor
    return `${Math.round(scaled)}${suffix}`
  }
  return value.toLocaleString()
}

export function tileTone(value: number): string {
  if (value <= 4) return 'soft'
  if (value <= 32) return 'warm'
  if (value <= 256) return 'gold'
  return 'bright'
}
