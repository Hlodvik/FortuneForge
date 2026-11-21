import { createGameCommandRecovery, PendingGameCommandError } from '@fortuneforge/client-presentation/game-command-recovery'
import { describe, expect, it, vi } from 'vitest'
import { HttpTwentyFortyEightGateway } from './httpTwentyFortyEightGateway'

const board = { gameId: 'saved-2048', size: 4, tiles: [2, 2, ...Array<number>(14).fill(0)], score: 0,
  moves: 0, highestTile: 2, phase: 'playing', canUndo: false, lastEvent: 'started', scoreGained: 0, message: 'Ready.' }

function storage() {
  const values = new Map<string, string>()
  return { getItem: (key: string) => values.get(key) ?? null,
    setItem: (key: string, value: string) => { values.set(key, value) }, removeItem: (key: string) => { values.delete(key) } }
}

describe('2048 durable gateway recovery', () => {
  it('remembers an explicitly read board for a later gateway without opening another game', async () => {
    const saved = storage()
    const recovery = () => createGameCommandRecovery({ accountId: 'alice', gameId: '2048', routePrefix: '/api/games/2048', storage: saved })
    const fetcher = vi.fn<(input: RequestInfo | URL, init?: RequestInit) => Promise<Response>>()
      .mockResolvedValueOnce(Response.json(board))
      .mockResolvedValueOnce(Response.json({ ...board, moves: 1 }))
    await new HttpTwentyFortyEightGateway('/api/games/2048', fetcher, recovery()).getGame(board.gameId)
    expect((await new HttpTwentyFortyEightGateway('/api/games/2048', fetcher, recovery()).recoverGame()).moves).toBe(1)
    expect(fetcher.mock.calls[1][0]).toBe(`/api/games/2048/games/${board.gameId}`)
    expect(fetcher.mock.calls[1][1]?.method).toBeUndefined()
  })

  it('keeps a lost start for its original account when a temporary account switch returns forbidden', async () => {
    const journal = createGameCommandRecovery({ accountId: 'alice', gameId: '2048', routePrefix: '/api/games/2048', storage: storage() })
    const fetcher = vi.fn<(input: RequestInfo | URL, init?: RequestInit) => Promise<Response>>()
      .mockRejectedValueOnce(new TypeError('Start response lost.'))
      .mockResolvedValueOnce(Response.json({ code: 'game-session-changed', message: 'The signed-in account changed.' }, { status: 403 }))
      .mockResolvedValueOnce(Response.json(board))
      .mockResolvedValueOnce(Response.json(board))
    const gateway = new HttpTwentyFortyEightGateway('/api/games/2048', fetcher, journal, 'alice')
    await expect(gateway.startGame(32)).rejects.toThrow('lost')
    const original = journal.read().pending
    await expect(gateway.recoverGame()).rejects.toThrow('account changed')
    expect(journal.read()).toEqual({ activeBoardId: null, pending: original })
    expect((await gateway.recoverGame()).gameId).toBe(board.gameId)
    expect(fetcher.mock.calls.slice(0, 3).map(([, init]) => new Headers(init?.headers).get('Idempotency-Key')))
      .toEqual([original?.idempotencyKey, original?.idempotencyKey, original?.idempotencyKey])
  })

  it('releases an expired legacy board only after a definite not-found so the next retry can start fresh', async () => {
    const legacyId = '9ca55a7b-6535-4dae-bfd3-518c63b41417'
    const journal = createGameCommandRecovery({ accountId: 'alice', gameId: '2048', routePrefix: '/api/games/2048', storage: storage() })
    journal.prepare(`/api/games/2048/games/${legacyId}/move`, '{"direction":"left"}')
    const fetcher = vi.fn<(input: RequestInfo | URL, init?: RequestInit) => Promise<Response>>()
      .mockResolvedValueOnce(Response.json({ code: '2048-game-not-found', message: 'That game is not available.' }, { status: 404 }))
      .mockResolvedValueOnce(Response.json(board))
    const gateway = new HttpTwentyFortyEightGateway('/api/games/2048', fetcher, journal)
    await expect(gateway.recoverGame()).rejects.toThrow('not available')
    expect(journal.read()).toEqual({ activeBoardId: null, pending: null })
    expect((await gateway.recoverGame()).gameId).toBe(board.gameId)
    expect(fetcher.mock.calls[1][0]).toBe('/api/games/2048/games')
  })

  it('reads a legacy host board after an uncertain move without applying that move twice', async () => {
    const legacyId = '9ca55a7b-6535-4dae-bfd3-518c63b41417'
    const journal = createGameCommandRecovery({ accountId: 'alice', gameId: '2048', routePrefix: '/api/games/2048', storage: storage() })
    journal.setActiveBoardId(legacyId)
    const fetcher = vi.fn<(input: RequestInfo | URL, init?: RequestInit) => Promise<Response>>()
      .mockRejectedValueOnce(new TypeError('Move response lost.'))
      .mockResolvedValueOnce(Response.json({ ...board, gameId: legacyId, moves: 1 }))
    const gateway = new HttpTwentyFortyEightGateway('/api/games/2048', fetcher, journal)
    await expect(gateway.move(legacyId, 'left')).rejects.toThrow('lost')
    expect((await gateway.move(legacyId, 'left')).moves).toBe(1)
    expect(fetcher.mock.calls[1][0]).toBe(`/api/games/2048/games/${legacyId}`)
    expect(fetcher.mock.calls[1][1]?.method).toBeUndefined()
    expect(journal.read()).toEqual({ activeBoardId: legacyId, pending: null })
  })

  it('recovers a lost start after remount with the original body and key, then loads the current board', async () => {
    const saved = storage()
    const recovery = () => createGameCommandRecovery({ accountId: 'alice', gameId: '2048', routePrefix: '/api/games/2048', storage: saved })
    const fetcher = vi.fn<(input: RequestInfo | URL, init?: RequestInit) => Promise<Response>>()
      .mockRejectedValueOnce(new TypeError('The response was lost.'))
      .mockResolvedValueOnce(Response.json(board))
      .mockResolvedValueOnce(Response.json({ ...board, moves: 3, canUndo: true }))
      .mockResolvedValueOnce(Response.json({ ...board, moves: 3, canUndo: true }))
    const first = new HttpTwentyFortyEightGateway('/api/games/2048', fetcher, recovery(), 'alice')
    await expect(first.startGame(32)).rejects.toThrow('lost')
    const remounted = new HttpTwentyFortyEightGateway('/api/games/2048', fetcher, recovery(), 'alice')
    expect((await remounted.recoverGame()).moves).toBe(3)
    const requests = fetcher.mock.calls
    expect(requests[1][0]).toBe(requests[0][0])
    expect(requests[1][1]?.body).toBe('{"seed":32}')
    expect(new Headers(requests[1][1]?.headers).get('Idempotency-Key')).toBe(new Headers(requests[0][1]?.headers).get('Idempotency-Key'))
    expect(new Headers(requests[1][1]?.headers).get('X-FortuneForge-Expected-Account')).toBe('alice')
    expect(requests[2][0]).toBe('/api/games/2048/games/saved-2048')
    await remounted.recoverGame()
    expect(fetcher.mock.calls[3][1]?.method).toBeUndefined()
    expect(recovery().read()).toEqual({ activeBoardId: board.gameId, pending: null })
  })

  it('keeps an ambiguous move ahead of a different move and rejects an invalid success response', async () => {
    const journal = createGameCommandRecovery({ accountId: 'alice', gameId: '2048', routePrefix: '/api/games/2048', storage: storage() })
    const fetcher = vi.fn<(input: RequestInfo | URL, init?: RequestInit) => Promise<Response>>()
      .mockResolvedValueOnce(Response.json({ code: 'worker-unavailable', message: 'Retry.' }, { status: 503 }))
      .mockResolvedValueOnce(Response.json({ moves: 1 }))
      .mockResolvedValueOnce(Response.json({ ...board, moves: 1 }))
      .mockResolvedValueOnce(Response.json({ ...board, moves: 2 }))
    const gateway = new HttpTwentyFortyEightGateway('/api/games/2048', fetcher, journal)
    await expect(gateway.move(board.gameId, 'left')).rejects.toThrow('Retry.')
    await expect(gateway.move(board.gameId, 'up')).rejects.toBeInstanceOf(PendingGameCommandError)
    expect(fetcher).toHaveBeenCalledTimes(1)
    await expect(gateway.recoverGame()).rejects.toThrow('invalid response')
    expect(journal.read().pending?.body).toBe('{"direction":"left"}')
    expect((await gateway.recoverGame()).moves).toBe(2)
    const keys = fetcher.mock.calls.slice(0, 3).map(([, init]) => new Headers(init?.headers).get('Idempotency-Key'))
    expect(new Set(keys).size).toBe(1)
    expect(journal.read().pending).toBeNull()
  })
})
