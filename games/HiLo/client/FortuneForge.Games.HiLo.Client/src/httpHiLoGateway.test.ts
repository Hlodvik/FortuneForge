import { describe, expect, it, vi } from 'vitest'
import { HttpHiLoGateway, isHiLoSession } from './httpHiLoGateway'
import { hiLoSessionFixture as session } from './testFixtures'

const json = (value: unknown, status = 200) => new Response(JSON.stringify(value), { status, headers: { 'Content-Type': 'application/json' } })
const make = (payload: unknown = session) => {
  const fetcher = vi.fn<typeof fetch>().mockResolvedValue(json(payload))
  return { fetcher, gateway: new HttpHiLoGateway('/custom/hi-lo/', fetcher) }
}

describe('HttpHiLoGateway', () => {
  it('loads only the cookie-bound authoritative session without caching', async () => {
    const { gateway, fetcher } = make()
    const signal = new AbortController().signal
    await expect(gateway.getSession(signal)).resolves.toEqual(session)
    expect(fetcher).toHaveBeenCalledWith('/custom/hi-lo/session', { signal, credentials: 'include', cache: 'no-store' })
    expect(fetcher.mock.contexts[0]).toBeUndefined()
  })

  it('posts integer cents and the exact operation ID with CSRF header', async () => {
    const { gateway, fetcher } = make()
    const signal = new AbortController().signal
    await gateway.startRound(100, { operationId: 'original-start-operation', signal })
    expect(fetcher).toHaveBeenCalledWith('/custom/hi-lo/rounds', {
      method: 'POST', signal, credentials: 'include', cache: 'no-store',
      headers: { 'Content-Type': 'application/json', 'X-FortuneForge-Game': 'hi-lo' },
      body: JSON.stringify({ operationId: 'original-start-operation', wagerCents: 100 }),
    })
  })

  it.each(['higher', 'lower', 'skip', 'cashout'] as const)('sends %s with its expected revision and no client outcome', async choice => {
    const { gateway, fetcher } = make({ ...session, round: { ...session.round!, revision: 1 } })
    await gateway.act(session.round!.roundId, choice, { operationId: 'exact-action-operation', expectedRevision: 0 })
    expect(fetcher).toHaveBeenCalledWith(`/custom/hi-lo/rounds/${session.round!.roundId}/actions`, expect.objectContaining({
      credentials: 'include', body: JSON.stringify({ operationId: 'exact-action-operation', expectedRevision: 0, choice }),
    }))
    const sent = JSON.parse(String(fetcher.mock.calls[0][1]?.body))
    expect(Object.keys(sent)).toEqual(['operationId', 'expectedRevision', 'choice'])
  })

  it('does not silently replay an ambiguous mutation or create another operation ID', async () => {
    const { gateway, fetcher } = make()
    fetcher.mockRejectedValue(new TypeError('Connection reset'))
    await expect(gateway.startRound(100, { operationId: 'same-original-key' })).rejects.toThrow('Connection reset')
    expect(fetcher).toHaveBeenCalledOnce()
  })

  it.each([
    ['fractional money', { ...session, walletBalanceCents: .1 }],
    ['unsafe money', { ...session, walletBalanceCents: Number.MAX_SAFE_INTEGER + 1 }],
    ['negative balance', { ...session, walletBalanceCents: -1 }],
    ['missing mode', { ...session, practice: undefined }],
    ['missing limit', { ...session, limits: { ...session.limits, houseEdge: undefined } }],
    ['inverted wager range', { ...session, limits: { ...session.limits, minWagerCents: 200000 } }],
    ['invalid card rank', { ...session, round: { ...session.round, currentCard: { rank: 'joker', suit: 'hearts' } } }],
    ['impossible chance', { ...session, round: { ...session.round, higher: { ...session.round!.higher, probability: 1.1 } } }],
    ['NaN multiplier', { ...session, round: { ...session.round, multiplier: Number.NaN } }],
    ['empty history', { ...session, round: { ...session.round, history: [] } }],
    ['mismatched revealed card', { ...session, round: { ...session.round, currentCard: { rank: 'ace', suit: 'hearts' } } }],
    ['active controls after settlement', { ...session, round: { ...session.round, status: 'cashedOut' } }],
    ['unpaid lost return', { ...session, round: { ...session.round, status: 'lost', canSkip: false, higher: { ...session.round!.higher, available: false }, lower: { ...session.round!.lower, available: false } } }],
  ])('rejects an incomplete or inconsistent response: %s', async (_label, payload) => {
    const { gateway } = make(payload)
    await expect(gateway.getSession()).rejects.toMatchObject({ code: 'hi-lo-invalid-response' })
  })

  it('accepts no active round and zero odds for an impossible choice', () => {
    expect(isHiLoSession({ ...session, round: null })).toBe(true)
    expect(isHiLoSession({ ...session, round: { ...session.round!, higher: { probability: 0, multiplier: 0, projectedReturnCents: 0, available: false } } })).toBe(true)
  })

  it('preserves an optional maintenance flag and rejects an unusable maintenance value', async () => {
    const disabled = { ...session, enabled: false }
    const { gateway } = make(disabled)
    await expect(gateway.getSession()).resolves.toEqual(disabled)
    expect(isHiLoSession({ ...session, enabled: true })).toBe(true)
    expect(isHiLoSession({ ...session, enabled: 'false' })).toBe(false)
  })

  it('rejects a valid response for another wager or another round revision', async () => {
    const { gateway } = make()
    await expect(gateway.startRound(200, { operationId: 'original-operation' })).rejects.toMatchObject({ code: 'hi-lo-invalid-response' })
    await expect(gateway.act(session.round!.roundId, 'higher', { operationId: 'original-operation', expectedRevision: 0 })).rejects.toMatchObject({ code: 'hi-lo-invalid-response' })
  })

  it('preserves definitive insufficient funds and conflict details for recovery', async () => {
    const { gateway, fetcher } = make()
    fetcher.mockResolvedValue(json({ code: 'insufficient_funds', message: 'Insufficient practice credits.' }, 409))
    await expect(gateway.startRound(100, { operationId: 'original-operation' })).rejects.toMatchObject({ status: 409, code: 'insufficient_funds', message: 'Insufficient practice credits.' })
  })

  it('treats unreadable 200 bodies as ambiguous and unreadable server errors as retryable', async () => {
    const { gateway, fetcher } = make()
    fetcher.mockResolvedValueOnce(new Response('truncated json', { status: 200 }))
      .mockResolvedValueOnce(new Response('temporary failure', { status: 503 }))
    await expect(gateway.getSession()).rejects.toMatchObject({ code: 'hi-lo-invalid-response', status: 200 })
    await expect(gateway.getSession()).rejects.toMatchObject({ status: 503 })
  })
})
