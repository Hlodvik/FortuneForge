export interface PendingGameCommand {
  readonly path: string
  readonly body: string
  readonly idempotencyKey: string
}

export interface GameCommandRecoveryState {
  readonly activeBoardId: string | null
  readonly pending: PendingGameCommand | null
}

export interface GameCommandRecoveryOptions {
  accountId: string
  gameId: string
  routePrefix: string
  storage?: Pick<Storage, 'getItem' | 'setItem' | 'removeItem'>
  createIdempotencyKey?: () => string
}

export interface GameCommandRecovery {
  read(): GameCommandRecoveryState
  setActiveBoardId(id: string | null): void
  prepare(path: string, body: string, preferredIdempotencyKey?: string): PendingGameCommand
  complete(command: PendingGameCommand, boardId?: string | null): boolean
}

export class PendingGameCommandError extends Error {
  constructor(readonly pending: PendingGameCommand) {
    super('Finish recovering the pending game command before starting another.')
    this.name = 'PendingGameCommandError'
  }
}

function isIdentifier(value: unknown): value is string {
  return typeof value === 'string' && value.trim().length > 0
}

function isPath(path: string): boolean {
  if (!path.startsWith('/') || /[\\?#\s]/u.test(path)) return false
  try {
    return path.slice(1).split('/').every(segment => {
      const decoded = decodeURIComponent(segment)
      return decoded.length > 0 && decoded !== '.' && decoded !== '..' && !/[\/\\\u0000-\u001f\u007f]/u.test(decoded)
    })
  } catch {
    return false
  }
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return value !== null && typeof value === 'object' && !Array.isArray(value)
}

/** A synchronous, per-account game command journal. Call complete only after a definitive response. */
export function createGameCommandRecovery(options: GameCommandRecoveryOptions): GameCommandRecovery {
  const { accountId, gameId, routePrefix } = options
  if (!isIdentifier(accountId) || !isIdentifier(gameId) || !isPath(routePrefix)) {
    throw new TypeError('Game command recovery needs an account, a game, and a same-origin route prefix.')
  }

  const storage = options.storage ?? globalThis.sessionStorage
  if (!storage) throw new Error('Game command recovery requires session storage.')
  const createIdempotencyKey = options.createIdempotencyKey ?? (() => globalThis.crypto.randomUUID())
  const key = `fortuneforge:game-command-recovery:v1:${encodeURIComponent(accountId)}:${encodeURIComponent(gameId)}`
  const inScope = (path: string) => isPath(path) && (path === routePrefix || path.startsWith(`${routePrefix}/`))
  const invalidState = () => new Error('The saved game command recovery state is invalid.')

  function read(): GameCommandRecoveryState {
    const saved = storage.getItem(key)
    if (saved === null) return { activeBoardId: null, pending: null }

    let value: unknown
    try {
      value = JSON.parse(saved)
    } catch {
      throw invalidState()
    }
    if (!isRecord(value) || (value.activeBoardId !== null && !isIdentifier(value.activeBoardId))) throw invalidState()
    const { activeBoardId, pending } = value
    if (pending === null) return { activeBoardId, pending: null }
    if (!isRecord(pending) || typeof pending.path !== 'string' || !inScope(pending.path) ||
        typeof pending.body !== 'string' || !isIdentifier(pending.idempotencyKey)) throw invalidState()
    return { activeBoardId, pending: Object.freeze({ path: pending.path, body: pending.body, idempotencyKey: pending.idempotencyKey }) }
  }

  function write(state: GameCommandRecoveryState): void {
    if (state.activeBoardId === null && state.pending === null) storage.removeItem(key)
    else storage.setItem(key, JSON.stringify(state))
  }

  function validateBoardId(id: string | null): void {
    if (id !== null && !isIdentifier(id)) throw new TypeError('An active board ID must be nonempty.')
  }

  return {
    read,
    setActiveBoardId(id) {
      validateBoardId(id)
      const state = read()
      if (state.activeBoardId === id) return
      if (state.pending) throw new PendingGameCommandError(state.pending)
      write({ activeBoardId: id, pending: null })
    },
    prepare(path, body, preferredIdempotencyKey) {
      if (!inScope(path)) throw new TypeError('The game command path is outside its route scope.')
      if (typeof body !== 'string') throw new TypeError('A game command needs its serialized request body.')
      const state = read()
      if (state.pending) {
        if (state.pending.path === path && state.pending.body === body) return state.pending
        throw new PendingGameCommandError(state.pending)
      }

      const idempotencyKey = preferredIdempotencyKey === undefined ? createIdempotencyKey() : preferredIdempotencyKey
      if (!isIdentifier(idempotencyKey)) throw new TypeError('A game command needs a nonempty idempotency key.')
      const pending = Object.freeze({ path, body, idempotencyKey })
      write({ activeBoardId: state.activeBoardId, pending })
      return pending
    },
    complete(command, boardId) {
      const state = read()
      if (!state.pending || state.pending.idempotencyKey !== command.idempotencyKey ||
          state.pending.path !== command.path || state.pending.body !== command.body) return false
      if (boardId !== undefined) validateBoardId(boardId)
      write({ activeBoardId: boardId === undefined ? state.activeBoardId : boardId, pending: null })
      return true
    },
  }
}
