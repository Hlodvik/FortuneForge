import { describe, expect, it } from 'vitest'
import { createGameCommandRecovery, PendingGameCommandError } from './gameCommandRecovery.js'

function memoryStorage() {
  const values = new Map<string, string>()
  return {
    values,
    getItem: (key: string) => values.get(key) ?? null,
    setItem: (key: string, value: string) => { values.set(key, value) },
    removeItem: (key: string) => { values.delete(key) },
  }
}

const routePrefix = '/api/games/2048'
const move = `${routePrefix}/games/board-1/move`
const start = `${routePrefix}/games`

function fixture(storage = memoryStorage()) {
  let key = 0
  return {
    storage,
    open: (accountId = 'account-1', gameId = '2048', prefix = routePrefix) => createGameCommandRecovery({
      accountId, gameId, routePrefix: prefix, storage, createIdempotencyKey: () => `request-${++key}`,
    }),
  }
}

describe('game command recovery', () => {
  it('recovers the active board and the exact ambiguous request after reload', () => {
    const sessions = fixture()
    const current = sessions.open()
    current.setActiveBoardId('board-1')
    const body = '{\n  "move": "←"\n}'
    const pending = current.prepare(move, body)
    expect(pending).toEqual({ path: move, body, idempotencyKey: 'request-1' })
    expect(Object.isFrozen(pending)).toBe(true)

    const reloaded = sessions.open()
    expect(reloaded.read()).toEqual({ activeBoardId: 'board-1', pending })
    expect(reloaded.prepare(move, body)).toEqual(pending)
    expect(reloaded.complete(pending)).toBe(true)
    expect(sessions.open().read()).toEqual({ activeBoardId: 'board-1', pending: null })
    expect(reloaded.prepare(move, body).idempotencyKey).toBe('request-2')
  })

  it('blocks different commands and unrelated board changes while one request is pending', () => {
    const sessions = fixture()
    const recovery = sessions.open()
    recovery.setActiveBoardId('board-1')
    const pending = recovery.prepare(move, '{"direction":"left"}')
    const current = sessions.open()
    expect(() => current.prepare(move, '{ "direction": "left" }')).toThrow(PendingGameCommandError)
    expect(() => current.prepare(start, '{}')).toThrow(PendingGameCommandError)
    expect(() => current.setActiveBoardId('board-2')).toThrow(PendingGameCommandError)
    current.setActiveBoardId('board-1')
    expect(current.complete({ ...pending, body: '{}' }, 'board-2')).toBe(false)
    expect(current.read()).toEqual({ activeBoardId: 'board-1', pending })
  })

  it('keeps the caller key and exact request across reload, recovery, and a later explicit replay', () => {
    const sessions = fixture()
    const callerKey = 'Caller:Match-42/V2'
    const body = '{\n  "move": "←"\n}'
    const pending = sessions.open().prepare(move, body, callerKey)
    expect(pending).toEqual({ path: move, body, idempotencyKey: callerKey })

    const reloaded = sessions.open()
    expect(reloaded.prepare(move, body, 'different-caller-key')).toEqual(pending)
    expect(reloaded.prepare(move, body, '')).toEqual(pending)
    expect(() => reloaded.prepare(move, '{"move":"←"}', callerKey)).toThrow(PendingGameCommandError)
    expect(reloaded.complete(pending)).toBe(true)
    expect(reloaded.prepare(move, body, callerKey)).toEqual(pending)
    expect(reloaded.complete(pending)).toBe(true)
    expect(reloaded.prepare(move, body).idempotencyKey).toBe('request-1')
  })

  it('rejects blank caller keys without changing storage or generating another key', () => {
    const sessions = fixture()
    const recovery = sessions.open()
    for (const callerKey of ['', ' \t\n']) {
      expect(() => recovery.prepare(start, '{}', callerKey)).toThrow('nonempty idempotency key')
    }
    expect(sessions.storage.values.size).toBe(0)
    expect(recovery.prepare(start, '{}', undefined).idempotencyKey).toBe('request-1')
  })

  it('publishes the confirmed start board and clears its request together', () => {
    const sessions = fixture()
    const recovery = sessions.open()
    const pending = recovery.prepare(start, '{}')
    expect(sessions.open().read()).toEqual({ activeBoardId: null, pending })
    expect(recovery.complete(pending, 'board-from-server')).toBe(true)
    const after = sessions.open()
    expect(after.read()).toEqual({ activeBoardId: 'board-from-server', pending: null })

    const next = after.prepare(start, '{}')
    expect(after.complete(pending, 'stale-response')).toBe(false)
    expect(after.complete(next, null)).toBe(true)
    expect(after.read()).toEqual({ activeBoardId: null, pending: null })
    expect(sessions.storage.values.size).toBe(0)
  })

  it('keeps accounts and games isolated and rejects routes outside the original scope', () => {
    const sessions = fixture()
    const first = sessions.open()
    const pending = first.prepare(move, '{}')
    expect(sessions.open('account-2').read().pending).toBeNull()
    expect(sessions.open('account-1', 'drop-merge', '/api/games/drop-merge').read().pending).toBeNull()
    expect(sessions.open('account-2').complete(pending)).toBe(false)
    expect(() => sessions.open('account-1', '2048', '/api/games/new-prefix').read()).toThrow('saved game command')
    expect(first.read().pending).toEqual(pending)

    const empty = sessions.open('account-2')
    for (const path of [
      '/api/games/2048-other/games', '/api/games/2048/../drop-merge/games', '/api/games/2048/%2e%2e/games',
      '/api/games/2048/board%2fother/move', 'https://example.com/api/games/2048/games',
    ]) expect(() => empty.prepare(path, '{}')).toThrow(TypeError)
    expect(empty.read().pending).toBeNull()
  })

  it('fails before sending a command when storage is unavailable or its saved state is malformed', () => {
    const storage = memoryStorage()
    const sessions = fixture(storage)
    const recovery = sessions.open()
    recovery.prepare(start, '{}')
    const savedKey = [...storage.values.keys()][0]
    storage.values.set(savedKey, '{ invalid')
    expect(() => sessions.open().prepare(move, '{}')).toThrow('saved game command')
    expect(storage.values.get(savedKey)).toBe('{ invalid')

    const cannotWrite = createGameCommandRecovery({
      accountId: 'other', gameId: '2048', routePrefix,
      storage: { ...storage, setItem() { throw new Error('storage quota') } },
      createIdempotencyKey: () => 'not-sent',
    })
    expect(() => cannotWrite.prepare(start, '{}')).toThrow('storage quota')
    expect(cannotWrite.read().pending).toBeNull()
  })
})
