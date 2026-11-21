import { describe, expect, it, vi } from 'vitest'
import { createGameCommandRecovery } from '@fortuneforge/client-presentation/game-command-recovery'
import { HttpSnakeGateway } from './httpSnakeGateway'

const game = { gameId: 'game-1', width: 16, height: 16, body: [{ x: 8, y: 8 }], food: { x: 2, y: 2 },
  direction: 'right', score: 0, bestScore: 0, moves: 1, length: 1, phase: 'playing', lastEvent: 'moved', scoreGained: 0, message: '' }
function journal(accountId = 'alice', saved = new Map<string, string>()) {
  return createGameCommandRecovery({ accountId, gameId: 'snake', routePrefix: '/api/games/snake',
    storage: { getItem: key => saved.get(key) ?? null, setItem: (key, value) => { saved.set(key, value) }, removeItem: key => { saved.delete(key) } } })
}

describe('Snake HTTP recovery', () => {
  it('replays an uncertain tick with its original key before GET on remount', async () => {
    const recovery = journal()
    recovery.setActiveBoardId('game-1')
    const firstFetch = vi.fn<typeof fetch>().mockRejectedValueOnce(new TypeError('response lost'))
    const first = new HttpSnakeGateway('/api/games/snake', firstFetch, recovery)
    await expect(first.tick('game-1')).rejects.toThrow('response lost')
    const key = new Headers(firstFetch.mock.calls[0][1]?.headers).get('Idempotency-Key')
    expect(key).toBeTruthy()
    const fetcher = vi.fn<typeof fetch>().mockResolvedValueOnce(new Response(JSON.stringify(game)))
      .mockResolvedValueOnce(new Response(JSON.stringify({ ...game, moves: 3 })))
    const replica = new HttpSnakeGateway('/api/games/snake', fetcher, recovery)
    expect((await replica.recoverGame()).moves).toBe(3)
    expect(fetcher.mock.calls[0][0]).toBe('/api/games/snake/games/game-1/tick')
    expect(new Headers(fetcher.mock.calls[0][1]?.headers).get('Idempotency-Key')).toBe(key)
    expect(fetcher.mock.calls[1][0]).toBe('/api/games/snake/games/game-1')
    expect(recovery.read().pending).toBeNull()
  })

  it('blocks another mutation until the uncertain command is resolved', async () => {
    const recovery = journal()
    const fetcher = vi.fn<typeof fetch>().mockRejectedValueOnce(new TypeError('offline'))
    const gateway = new HttpSnakeGateway('/api/games/snake', fetcher, recovery)
    await expect(gateway.tick('game-1')).rejects.toThrow('offline')
    await expect(gateway.reset('game-1')).rejects.toThrow('Finish recovering')
    expect(fetcher).toHaveBeenCalledTimes(1)
  })

  it('restores saved games within the authenticated account only', async () => {
    const storage = new Map<string, string>()
    const recovery = journal('alice', storage)
    recovery.setActiveBoardId('game-1')
    expect(journal('bob', storage).read().activeBoardId).toBeNull()
    const fetcher = vi.fn<typeof fetch>().mockResolvedValueOnce(new Response(JSON.stringify(game)))
    expect((await new HttpSnakeGateway('/api/games/snake', fetcher, journal('alice', storage)).recoverGame()).moves).toBe(1)
    expect(fetcher.mock.calls[0][0]).toBe('/api/games/snake/games/game-1')
    expect(fetcher.mock.calls[0][1]?.method).toBeUndefined()
  })
})


it('binds a pending command to its opening account and retains it after an account change', async () => {
  const recovery = journal()
  const fetcher = vi.fn<typeof fetch>().mockResolvedValueOnce(new Response(JSON.stringify({ code: 'game-session-changed', message: 'Sign in again.' }), { status: 403 }))
  await expect(new HttpSnakeGateway('/api/games/snake', fetcher, recovery, 'alice').tick('game-1')).rejects.toMatchObject({ status: 403 })
  expect(new Headers(fetcher.mock.calls[0][1]?.headers).get('X-FortuneForge-Expected-Account')).toBe('alice')
  expect(recovery.read().pending?.path).toBe('/api/games/snake/games/game-1/tick')
})
