import { afterEach, describe, expect, it, vi } from 'vitest'
import { KenoGatewayError, type KenoRoundRequest } from './contracts'
import { HttpKenoGateway } from './httpKenoGateway'

const request: KenoRoundRequest = { ticket: { numbers: [3, 7, 15] } }
const round = {
  roundId: 'keno-11', balance: 1_000, phase: 'completed',
  ticket: request.ticket,
  draw: { numbers: [3, 7, 21, 22, 23, 24, 25, 26, 27, 28, 29, 30, 31, 32, 33, 34, 35, 36, 37, 38] },
  hitCount: 2,
  outcome: 'two hits',
}

afterEach(() => vi.unstubAllGlobals())

describe('HttpKenoGateway', () => {
  it('requests authenticated Keno status', async () => {
    const fetchMock = vi.fn().mockResolvedValue(jsonResponse({ available: true, balance: 1_000, mode: 'free-play-keno' }))
    vi.stubGlobal('fetch', fetchMock)

    await expect(new HttpKenoGateway().getStatus()).resolves.toMatchObject({ available: true })
    expect(fetchMock).toHaveBeenCalledWith('/api/games/keno/status', { signal: undefined })
  })

  it('posts a chosen-number ticket and maps the completed round JSON', async () => {
    const fetchMock = vi.fn().mockResolvedValue(jsonResponse(round))
    vi.stubGlobal('fetch', fetchMock)

    await expect(new HttpKenoGateway().createRound(request)).resolves.toEqual(round)
    expect(fetchMock).toHaveBeenCalledWith('/api/games/keno/rounds', expect.objectContaining({
      method: 'POST', headers: expect.objectContaining({ 'content-type': 'application/json', 'Idempotency-Key': expect.stringMatching(/^keno-/) }), body: JSON.stringify(request), signal: undefined,
    }))
  })

  it('uses a caller-supplied key so a Keno draw can be retried safely', async () => {
    const fetchMock = vi.fn().mockResolvedValue(jsonResponse(round))
    vi.stubGlobal('fetch', fetchMock)

    await new HttpKenoGateway().createRound(request, { idempotencyKey: 'keno-retry-0001' })

    expect(fetchMock).toHaveBeenCalledWith('/api/games/keno/rounds', expect.objectContaining({
      headers: expect.objectContaining({ 'Idempotency-Key': 'keno-retry-0001' }),
    }))
  })

  it('preserves non-success responses as typed gateway errors', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(jsonResponse({ code: 'keno-ticket-invalid', message: 'Choose up to ten numbers.' }, 400)))

    await expect(new HttpKenoGateway().createRound(request)).rejects.toMatchObject({
      name: 'KenoGatewayError', code: 'keno-ticket-invalid', status: 400, message: 'Choose up to ten numbers.',
    })
  })

  it('rejects malformed successful round responses', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(jsonResponse({ ...round, hitCount: 3 })))

    await expect(new HttpKenoGateway().createRound(request)).rejects.toBeInstanceOf(KenoGatewayError)
  })
})

function jsonResponse(body: unknown, status = 200): Response {
  return new Response(JSON.stringify(body), { status, headers: { 'content-type': 'application/json' } })
}
