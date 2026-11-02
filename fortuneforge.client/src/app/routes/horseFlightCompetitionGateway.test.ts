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
      runId: freeRunId, seed: 42, tickMilliseconds: 20, recoveryScope: JSON.stringify(['free']),
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

  it('exposes explicit mode and canonical window identity before a paid admission', async () => {
    const shared = gateway(), adapter = new HorseFlightCompetitionGatewayAdapter(shared, { kind: 'paid', period: 'weekly' }, 200)
    expect(await adapter.getStatus()).toMatchObject({ recoveryMode: 'paid:weekly', recoveryScope: weeklyScope() })
    expect(shared.startHorseFlightAttempt).not.toHaveBeenCalled()
  })

  it('does not replay a previous admission key into a new competition window', async () => {
    const shared = gateway(), adapter = new HorseFlightCompetitionGatewayAdapter(shared, { kind: 'paid', period: 'weekly' }, 200)
    const before = await adapter.getStatus()
    vi.mocked(shared.getLeaderboard).mockResolvedValue({ ...window('weekly'), startsAtUtc: '2026-10-15T12:00:00Z', endsAtUtc: '2026-10-22T12:00:00Z' })
    await expect(adapter.start({ idempotencyKey: 'horse-flight-paid-012345', recoveryScope: before.recoveryScope })).rejects.toThrow('window changed')
    expect(shared.startHorseFlightAttempt).not.toHaveBeenCalled()
  })

  it('requires the same window even after reconstructing a new adapter on reload', async () => {
    const shared = gateway()
    vi.mocked(shared.getLeaderboard).mockResolvedValue({ ...window('weekly'), startsAtUtc: '2026-10-15T12:00:00Z', endsAtUtc: '2026-10-22T12:00:00Z' })
    const adapter = new HorseFlightCompetitionGatewayAdapter(shared, { kind: 'paid', period: 'weekly' }, 200)
    await adapter.getStatus()
    await expect(adapter.start({ idempotencyKey: 'horse-flight-paid-012345', recoveryScope: weeklyScope() })).rejects.toThrow('window changed')
    expect(shared.startHorseFlightAttempt).not.toHaveBeenCalled()
  })

  it('rejects a daily replay scope through a weekly gateway before publication', async () => {
    const shared = gateway(), adapter = new HorseFlightCompetitionGatewayAdapter(shared, { kind: 'paid', period: 'weekly' }, 200)
    await expect(adapter.complete(paidRunId, 30, [2], { recoveryScope: JSON.stringify(['paid:daily', '2026-10-08T12:00:00.000Z', '2026-10-09T12:00:00.000Z']) }, [8])).rejects.toThrow('window changed')
    expect(shared.completeHorseFlightReplay).not.toHaveBeenCalled()
  })

  it('records the actual accepted window when the first admission crosses its boundary', async () => {
    const shared = gateway(), adapter = new HorseFlightCompetitionGatewayAdapter(shared, { kind: 'paid', period: 'weekly' }, 200)
    const old = await adapter.getStatus()
    vi.mocked(shared.startHorseFlightAttempt).mockResolvedValue({ attemptId: 'attempt-1', gameId: 'horse-flight', period: 'weekly',
      startsAtUtc: '2026-10-15T12:00:00Z', endsAtUtc: '2026-10-22T12:00:00Z', entryFeeCents: 100, wasReplay: false, runId: paidRunId, seed: 77 })
    const accepted = await adapter.start({ idempotencyKey: 'horse-flight-paid-012345', recoveryScope: old.recoveryScope })
    expect(accepted.recoveryScope).toBe(JSON.stringify(['paid:weekly', '2026-10-15T12:00:00.000Z', '2026-10-22T12:00:00.000Z']))
    expect(shared.startHorseFlightAttempt).toHaveBeenCalledTimes(1)
  })
  it('keeps entry availability separate from backend completion of an already accepted same-window run', async () => {
    const shared = gateway()
    vi.mocked(shared.getLeaderboard).mockResolvedValue({ ...window('weekly'), entriesOpen: false })
    const adapter = new HorseFlightCompetitionGatewayAdapter(shared, { kind: 'paid', period: 'weekly' }, 200)
    expect(await adapter.getStatus()).toMatchObject({ available: false, recoveryScope: weeklyScope() })
    await expect(adapter.start({ idempotencyKey: 'horse-flight-paid-012345', recoveryScope: weeklyScope() })).rejects.toThrow('closed to new entries')
    expect(shared.startHorseFlightAttempt).not.toHaveBeenCalled()
    await adapter.complete(paidRunId, 30, [2], { recoveryScope: weeklyScope() }, [8])
    expect(shared.completeHorseFlightReplay).toHaveBeenCalledTimes(1)
  })
})

function weeklyScope() { return JSON.stringify(['paid:weekly', '2026-10-08T12:00:00.000Z', '2026-10-15T12:00:00.000Z']) }
function window(period: 'daily' | 'weekly') {
  return { gameId: 'horse-flight', period, startsAtUtc: '2026-10-08T12:00:00Z', endsAtUtc: '2026-10-15T12:00:00Z',
    entriesOpen: true, isCompleted: false, totalUniquePlayers: 2, leaderboard: [], solePlayerRefund: null, visibleJackpotCents: 200 }
}

function gateway(): HorseFlightCompetitionGateway {
  return {
    getLeaderboard: vi.fn().mockImplementation(async (_game, period: 'daily' | 'weekly') => window(period)),
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
