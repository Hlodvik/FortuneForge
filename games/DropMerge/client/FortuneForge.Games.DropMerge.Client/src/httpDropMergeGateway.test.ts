import { createGameCommandRecovery } from '@fortuneforge/client-presentation/game-command-recovery'
import { describe, expect, it, vi } from 'vitest'
import { HttpDropMergeGateway } from './httpDropMergeGateway'

const board = { gameId: 'saved-drop-merge', columns: 7, rows: 7, tiles: Array<number>(49).fill(0), currentTile: 2,
  nextTile: 4, score: 0, moves: 0, combo: 0, bestCombo: 0, totalMerges: 0, smallestTileClearCount: 0,
  nextBigTile: 1024, highestTile: 0, emptyTileCount: 49, phase: 'playing', canUndo: false, lastEvent: 'started',
  scoreGained: 0, mergeCount: 0, dropColumn: -1, dropRow: -1, removedTile: 0, introducedTile: 0, mergeSteps: [],
  tempoLevel: 0, dropDurationMilliseconds: 680, dropTimerMilliseconds: 10000, message: 'Ready.' }

function storage() {
  const values = new Map<string, string>()
  return { getItem: (key: string) => values.get(key) ?? null,
    setItem: (key: string, value: string) => { values.set(key, value) }, removeItem: (key: string) => { values.delete(key) } }
}

describe('Drop Merge durable gateway recovery', () => {
  it('retains a lost start and its exact command when the current account becomes forbidden', async () => {
    const journal = createGameCommandRecovery({ accountId: 'alice', gameId: 'drop-merge', routePrefix: '/api/games/drop-merge', storage: storage() })
    const fetcher = vi.fn<(input: RequestInfo | URL, init?: RequestInit) => Promise<Response>>()
      .mockRejectedValueOnce(new TypeError('Start response lost.'))
      .mockResolvedValueOnce(Response.json({ code: 'game-session-changed', message: 'The signed-in account changed.' }, { status: 403 }))
      .mockResolvedValueOnce(Response.json(board))
      .mockResolvedValueOnce(Response.json(board))
    const gateway = new HttpDropMergeGateway('/api/games/drop-merge', fetcher, journal, 'alice')
    await expect(gateway.startGame(41)).rejects.toThrow('lost')
    const original = journal.read().pending
    await expect(gateway.recoverGame()).rejects.toThrow('account changed')
    expect(journal.read()).toEqual({ activeBoardId: null, pending: original })
    expect((await gateway.recoverGame()).gameId).toBe(board.gameId)
    expect(fetcher.mock.calls.slice(0, 3).map(([, init]) => new Headers(init?.headers).get('Idempotency-Key')))
      .toEqual([original?.idempotencyKey, original?.idempotencyKey, original?.idempotencyKey])
  })

  it('reads a legacy host reset result without starting another random run', async () => {
    const legacyId = 'c4a28d8c-3fae-44fa-8e13-f80846ca4260'
    const journal = createGameCommandRecovery({ accountId: 'alice', gameId: 'drop-merge', routePrefix: '/api/games/drop-merge', storage: storage() })
    const fetcher = vi.fn<(input: RequestInfo | URL, init?: RequestInit) => Promise<Response>>()
      .mockRejectedValueOnce(new TypeError('Reset response lost.'))
      .mockResolvedValueOnce(Response.json({ ...board, gameId: legacyId, currentTile: 8 }))
    const gateway = new HttpDropMergeGateway('/api/games/drop-merge', fetcher, journal)
    await expect(gateway.reset(legacyId)).rejects.toThrow('lost')
    expect((await gateway.reset(legacyId)).currentTile).toBe(8)
    expect(fetcher.mock.calls[1][0]).toBe(`/api/games/drop-merge/games/${legacyId}`)
    expect(fetcher.mock.calls[1][1]?.method).toBeUndefined()
    expect(journal.read().pending).toBeNull()
  })

  it('replays an empty-body undo after reload and restores the saved queue and timer', async () => {
    const saved = storage()
    const recovery = () => createGameCommandRecovery({ accountId: 'alice', gameId: 'drop-merge', routePrefix: '/api/games/drop-merge', storage: saved })
    recovery().setActiveBoardId(board.gameId)
    const fetcher = vi.fn<(input: RequestInfo | URL, init?: RequestInit) => Promise<Response>>()
      .mockRejectedValueOnce(new TypeError('Connection lost.'))
      .mockResolvedValueOnce(Response.json({ ...board, lastEvent: 'undone' }))
      .mockResolvedValueOnce(Response.json({ ...board, currentTile: 8, nextTile: 16, dropTimerMilliseconds: 9750 }))
    await expect(new HttpDropMergeGateway('/api/games/drop-merge', fetcher, recovery(), 'alice').undo(board.gameId)).rejects.toThrow('lost')
    const result = await new HttpDropMergeGateway('/api/games/drop-merge', fetcher, recovery(), 'alice').recoverGame()
    expect(result.currentTile).toBe(8)
    expect(result.nextTile).toBe(16)
    expect(result.dropTimerMilliseconds).toBe(9750)
    expect(fetcher.mock.calls[1][1]?.body).toBe('')
    expect(new Headers(fetcher.mock.calls[1][1]?.headers).get('Idempotency-Key')).toBe(new Headers(fetcher.mock.calls[0][1]?.headers).get('Idempotency-Key'))
    expect(new Headers(fetcher.mock.calls[1][1]?.headers).get('X-FortuneForge-Expected-Account')).toBe('alice')
    expect(recovery().read().pending).toBeNull()
  })

  it('retains throttled commands, clears a definite rule rejection, and isolates another account', async () => {
    const saved = storage()
    const alice = createGameCommandRecovery({ accountId: 'alice', gameId: 'drop-merge', routePrefix: '/api/games/drop-merge', storage: saved })
    const bob = createGameCommandRecovery({ accountId: 'bob', gameId: 'drop-merge', routePrefix: '/api/games/drop-merge', storage: saved })
    const fetcher = vi.fn<(input: RequestInfo | URL, init?: RequestInit) => Promise<Response>>()
      .mockResolvedValueOnce(Response.json({ code: 'rate-limited', message: 'Wait.' }, { status: 429 }))
      .mockResolvedValueOnce(Response.json({ code: 'invalid-column', message: 'Invalid.' }, { status: 400 }))
      .mockResolvedValueOnce(Response.json(board))
    const gateway = new HttpDropMergeGateway('/api/games/drop-merge', fetcher, alice)
    await expect(gateway.drop(board.gameId, 9)).rejects.toThrow('Wait.')
    expect(alice.read().pending?.body).toBe('{"column":9}')
    expect(bob.read()).toEqual({ activeBoardId: null, pending: null })
    await expect(gateway.recoverGame()).rejects.toThrow('Invalid.')
    expect(alice.read().pending).toBeNull()
    await new HttpDropMergeGateway('/api/games/drop-merge', fetcher, bob).recoverGame()
    expect(fetcher.mock.calls[2][0]).toBe('/api/games/drop-merge/games')
    expect(new Headers(fetcher.mock.calls[2][1]?.headers).get('Idempotency-Key')).not.toBe(new Headers(fetcher.mock.calls[0][1]?.headers).get('Idempotency-Key'))
  })
})
