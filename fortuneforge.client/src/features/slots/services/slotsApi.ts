import { isSlotSymbolId, type SpinResult } from '../types/slots'

type SpinRequest = {
  gameId: string
  wagerPoints: number
}

export async function requestSpin(request: SpinRequest): Promise<SpinResult> {
  const response = await fetch('/api/slots/spins', {
    method: 'POST',
    headers: {
      'Content-Type': 'application/json',
    },
    body: JSON.stringify(request),
  })

  if (!response.ok) {
    const problem = (await response.json().catch(() => null)) as { error?: string; detail?: string } | null
    throw new Error(problem?.error ?? problem?.detail ?? `Spin request failed with status ${response.status}.`)
  }

  const result = (await response.json()) as SpinResult
  if (!isSpinResult(result)) {
    throw new Error('The spin server returned an invalid reel result.')
  }

  return result
}

function isSpinResult(result: SpinResult): boolean {
  return (
    Array.isArray(result.reels) &&
    result.reels.length > 0 &&
    result.reels.every(
      (reel) => Array.isArray(reel) && reel.length > 0 && reel.every(isSlotSymbolId),
    ) &&
    Array.isArray(result.reelStops) &&
    result.reelStops.length === result.reels.length
  )
}
