import type { PharaohSlotState, PharaohSpinResult } from '../src/types'
import type { TombRun, TombState } from '../src/tomb'
import { PHARAOH_MATH } from '../src/math'

export const ticketId = 'tomb-test-ticket-0001'
export function tombState(revision = 0): TombState {
  return { contractVersion: 1, revision, funding: { sigils: 0, requiredSigils: 6, averageWagerPoints: 0,
    minimumScarabs: PHARAOH_MATH.tombMinimumScarabs },
    tickets: [], activeRun: null, lastRun: null, pendingAction: null }
}
export function tombRun(): TombRun {
  return { id: ticketId, fundedWagerPoints: 12, torchesRemaining: 6, keys: 0,
    relics: { sun: 0, moon: 0, serpent: 0 }, relicMultiplier: 1, roomPoints: 0, bankedPoints: 60,
    phase: 'explore', path: [], choices: [
      { id: ticketId + '-0-coin', kind: 'coin', relic: null },
      { id: ticketId + '-0-relic', kind: 'relic', relic: 'sun' },
      { id: ticketId + '-0-key', kind: 'key', relic: null },
    ] }
}
export function slotState(revision = 0, tomb: TombState = tombState(revision)): PharaohSlotState {
  const collections = (['sync', 'rows', 'paw', 'rand'] as const).map(sealId => ({ sealId, count: 0, requiredCount: 24, averageWagerPoints: 0 }))
  return { freeSpinsRemaining: 0, freeSpinWagerPoints: null, freeSpinFeatureMode: null,
    specialPointsBalance: 0, energyBalance: 0, slotsCreditsBalance: 100, sealCollections: collections,
    featureActionRequired: Boolean(tomb.activeRun || tomb.lastRun || tomb.pendingAction),
    gameFeatureState: { kind: 'pharaoh-ascent', collections, freeSpinsRemaining: 0, freeSpinWagerPoints: null,
      freeSpinFeatureMode: null, energyBalance: 0, tomb } }
}
export function spinResult(revision = 1): PharaohSpinResult {
  return { ...slotState(revision), spinId: 'test-spin-0000000000001', gameId: 'pharaohs-ascent-v1',
    reelSetId: 'classic-reels-v4', symbolSetId: 'classic-symbols-v3', paytableId: 'classic-paytable-v4',
    wagerPoints: 12, pointValueInCents: 25, reels: [
      ['2', '3', '4', 'BOLT'], ['3', '4', '5', '6'], ['4', '5', '6', '7'], ['5', '6', '7', 'ACE'], ['FREE', '7', 'ACE', 'POWER'],
    ], reelStops: [0, 0, 0, 0, 0], payout: { totalPoints: 0, paylines: [] }, isFreeSpin: false,
    chargedWagerCents: 300, spinFeature: null, freeSpinsAwarded: 0 }
}
export function jsonReply(value: unknown, status = 200): Response {
  return new Response(JSON.stringify(value), { status, headers: { 'Content-Type': 'application/json', 'X-FortuneForge-Prepared-Spin': '1' } })
}
