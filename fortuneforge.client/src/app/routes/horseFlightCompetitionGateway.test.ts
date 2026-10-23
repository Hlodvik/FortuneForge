import { describe, expect, it, vi } from 'vitest'
import type { HorseFlightCompetitionGateway } from '../../games/arcade/arcadeCompetitionApi'
import { HorseFlightCompetitionGatewayAdapter } from './horseFlightCompetitionGateway'

const freeRunId = 'horse_flight_free_0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef'
const paidRunId = 'horse_flight_0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef'

describe('HorseFlightCompetitionGatewayAdapter', () => {
  it('routes free starts and replay completion without trusting a client score', async () => {
    const shared = gateway()
    const adapter = new HorseFlightCompetitionGatewayAdapter(shared, { kind: 'free' }, 200)

    expect(await adapter.start({ idempotencyKey: 'horse-flight-free-012345' })).toEqual({
      runId: freeRunId, seed: 42, tickMilliseconds: 20,
    })
    await adapter.complete(freeRunId, 30, [2], {}, [8])

    expect(shared.completeFreeHorseFlightReplay).toHaveBeenCalledWith(
      freeRunId,
      { totalTicks: 30, jumpTicks: [2], rightClickTicks: [8] },
      undefined,
    )
  })

  it('routes ranked runs to their selected period and refreshes the account', async () => {
    const shared = gateway()
    const refresh = vi.fn()
    const adapter = new HorseFlightCompetitionGatewayAdapter(shared, { kind: 'paid', period: 'weekly' }, 200, refresh)

    await adapter.start({ idempotencyKey: 'horse-flight-paid-012345' })
    await adapter.complete(paidRunId, 30, [2], {}, [8])

    expect(shared.startHorseFlightAttempt).toHaveBeenCalledWith('weekly', 'horse-flight-paid-012345', undefined)
    expect(shared.completeHorseFlightReplay).toHaveBeenCalledWith(
      'weekly', paidRunId, { totalTicks: 30, jumpTicks: [2], rightClickTicks: [8] }, undefined,
    )
    expect(refresh).toHaveBeenCalledTimes(2)
  })
})

function gateway(): HorseFlightCompetitionGateway {
  return {
    getLeaderboard: vi.fn(),
    startFreeHorseFlightRun: vi.fn().mockResolvedValue({
      runId: freeRunId, seed: 42, wasReplay: false,
    }),
    completeFreeHorseFlightReplay: vi.fn().mockResolvedValue({
      runId: freeRunId, score: 9, phase: 'obstacle-collision', wasReplay: false,
    }),
    startHorseFlightAttempt: vi.fn().mockResolvedValue({
      attemptId: 'attempt-1', gameId: 'horse-flight', period: 'weekly',
      startsAtUtc: '2026-10-08T12:00:00Z', endsAtUtc: '2026-10-15T12:00:00Z',
      entryFeeCents: 100, wasReplay: false, runId: paidRunId, seed: 77,
    }),
    completeHorseFlightReplay: vi.fn().mockResolvedValue({
      runId: paidRunId, score: 11, phase: 'fell', wasReplay: false,
    }),
  }
}
