import { describe, expect, it, vi } from 'vitest'
import { HorseFlightGatewayError } from './contracts'
import { HttpHorseFlightGateway } from './httpHorseFlightGateway'

describe('HttpHorseFlightGateway', () => {
  it('uses caller-supplied keys for a run start and its terminal replay', async () => {
    const fetchMock = vi.fn()
      .mockResolvedValueOnce(response({ runId: 'run-11', seed: 17, tickMilliseconds: 20 }))
      .mockResolvedValueOnce(response({ runId: 'run-11', balance: 1_000, score: 42, phase: 'fell', totalTicks: 48, jumpTicks: [0], rightClickTicks: [2] }))
    const gateway = new HttpHorseFlightGateway('/api/games/horse-flight', fetchMock)

    await gateway.start({ idempotencyKey: 'horse-flight-start-retry-0001' })
    await gateway.complete('run-11', 48, [0], { idempotencyKey: 'horse-flight-complete-retry-0001' }, [2])

    expect(fetchMock).toHaveBeenNthCalledWith(1, '/api/games/horse-flight/runs', expect.objectContaining({ headers: expect.objectContaining({ 'Idempotency-Key': 'horse-flight-start-retry-0001' }) }))
    expect(fetchMock).toHaveBeenNthCalledWith(2, '/api/games/horse-flight/runs/run-11/complete', expect.objectContaining({ headers: expect.objectContaining({ 'Idempotency-Key': 'horse-flight-complete-retry-0001' }) }))
    expect(JSON.parse(fetchMock.mock.calls[1][1].body)).toEqual({ totalTicks: 48, jumpTicks: [0], rightClickTicks: [2] })
  })

  it('preserves structured availability errors for the player UI', async () => {
    const gateway = new HttpHorseFlightGateway('/api/games/horse-flight', vi.fn().mockResolvedValue(response({ code: 'horse-flight-disabled', message: 'Not ready.' }, 503)))

    await expect(gateway.getStatus()).rejects.toMatchObject({
      name: 'HorseFlightGatewayError', code: 'horse-flight-disabled', status: 503,
    } satisfies Partial<HorseFlightGatewayError>)
  })
})

function response(body: unknown, status = 200) { return new Response(JSON.stringify(body), { status, headers: { 'content-type': 'application/json' } }) }
