export type AsteroidsPaidAdmission = Readonly<{
  expectedWindowStartsAtUtc: string
  expectedWindowEndsAtUtc: string
}> | Readonly<{ recoverOnly: true }>

/** Freezes the intended competition without comparing it with a later browser clock. */
export function createAsteroidsPaidAdmission(window: Readonly<{ startsAtUtc: string; endsAtUtc: string }>): AsteroidsPaidAdmission {
  if (!isUtcTime(window.startsAtUtc) || !isUtcTime(window.endsAtUtc) || Date.parse(window.endsAtUtc) <= Date.parse(window.startsAtUtc)) {
    throw new Error('The Asteroids competition window is invalid.')
  }
  return { expectedWindowStartsAtUtc: window.startsAtUtc, expectedWindowEndsAtUtc: window.endsAtUtc }
}

/** Older browser caches have no frozen window and may only recover an existing admission. */
export function readAsteroidsPaidAdmission(value: unknown): AsteroidsPaidAdmission {
  if (typeof value !== 'object' || value === null || Array.isArray(value)) return { recoverOnly: true }
  const record = value as Record<string, unknown>
  if (record.recoverOnly === true) return { recoverOnly: true }
  if (isUtcTime(record.expectedWindowStartsAtUtc) && isUtcTime(record.expectedWindowEndsAtUtc) &&
      Date.parse(record.expectedWindowEndsAtUtc) > Date.parse(record.expectedWindowStartsAtUtc)) {
    return { expectedWindowStartsAtUtc: record.expectedWindowStartsAtUtc, expectedWindowEndsAtUtc: record.expectedWindowEndsAtUtc }
  }
  return { recoverOnly: true }
}

function isUtcTime(value: unknown): value is string {
  return typeof value === 'string' && /(?:Z|\+00:00)$/.test(value) && Number.isFinite(Date.parse(value))
}
