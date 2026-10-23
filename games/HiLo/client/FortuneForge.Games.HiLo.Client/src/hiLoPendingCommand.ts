import { HiLoGatewayError, type HiLoChoice, type HiLoGateway, type HiLoSession } from './contracts'

export type HiLoPendingCommand = Readonly<
  | { kind: 'start'; operationId: string; wagerCents: number }
  | { kind: 'action'; operationId: string; roundId: string; expectedRevision: number; choice: HiLoChoice }
>
export type HiLoCommandStorage = Pick<Storage, 'getItem' | 'setItem' | 'removeItem'>

/** Stores only a request, never a card deck, future result, or wallet balance. */
export class HiLoPendingOperationStore {
  private readonly key: string
  private readonly storage: HiLoCommandStorage

  constructor(readonly playerId: string, storage?: HiLoCommandStorage) {
    if (!playerId.trim()) throw new Error('A player-and-mode scope is required for Hi-Lo recovery.')
    this.key = `fortuneforge:hi-lo:pending:v1:${encodeURIComponent(playerId)}`
    try {
      const browserStorage = storage ?? (typeof sessionStorage === 'undefined' ? undefined : sessionStorage)
      if (!browserStorage) throw new Error('No session storage')
      this.storage = browserStorage
    } catch {
      throw new HiLoGatewayError('Allow session storage to keep wagers recoverable after refresh.', 'hi-lo-storage-unavailable')
    }
  }

  read(): HiLoPendingCommand | null {
    try {
      const raw = this.storage.getItem(this.key)
      if (raw === null) return null
      const value: unknown = JSON.parse(raw)
      if (!record(value) || value.version !== 1 || value.scope !== this.playerId || !isCommand(value.command)) {
        throw new Error('Unrecognized command')
      }
      return value.command
    } catch (error) {
      if (error instanceof HiLoGatewayError) throw error
      throw new HiLoGatewayError('The saved Hi-Lo action could not be read. Restore the table before placing another wager.', 'hi-lo-pending-invalid')
    }
  }

  write(command: HiLoPendingCommand): void {
    if (!isCommand(command)) throw new HiLoGatewayError('This Hi-Lo action is incomplete.', 'hi-lo-invalid-command')
    const existing = this.read()
    if (existing && JSON.stringify(existing) !== JSON.stringify(command)) {
      throw new HiLoGatewayError('Restore the pending Hi-Lo action before starting another.', 'hi-lo-pending-exists')
    }
    try {
      this.storage.setItem(this.key, JSON.stringify({ version: 1, scope: this.playerId, command }))
      // Storage can silently reject writes in some embedded browsers.
      if (JSON.stringify(this.read()) !== JSON.stringify(command)) throw new Error('Request was not saved')
    } catch {
      throw new HiLoGatewayError('Hi-Lo could not save this action for recovery. Allow session storage and retry.', 'hi-lo-storage-unavailable')
    }
  }

  clear(operationId: string): void {
    const existing = this.read()
    if (!existing || existing.operationId !== operationId) return
    try { this.storage.removeItem(this.key) }
    catch { throw new HiLoGatewayError('Hi-Lo could not clear the recovered action. Retry recovery.', 'hi-lo-storage-unavailable') }
  }
}

/** Secure identifier only; paid-card randomness always lives on the server. */
export function createHiLoOperationId(): string {
  if (typeof crypto === 'undefined' || typeof crypto.randomUUID !== 'function') {
    throw new HiLoGatewayError('A secure browser context is required to place a Hi-Lo wager.', 'hi-lo-secure-context-required')
  }
  return crypto.randomUUID()
}

export function executeHiLoCommand(gateway: HiLoGateway, command: HiLoPendingCommand, signal?: AbortSignal): Promise<HiLoSession> {
  return command.kind === 'start'
    ? gateway.startRound(command.wagerCents, { operationId: command.operationId, signal })
    : gateway.act(command.roundId, command.choice, { operationId: command.operationId, expectedRevision: command.expectedRevision, signal })
}

/** Unknown transport outcomes and invalid success bodies must retain the exact operation. */
export function isHiLoRecoverableError(error: unknown): boolean {
  // The server replays an already-accepted key before checking maintenance.
  // This specific rejection therefore proves that no mutation was committed.
  if (error instanceof HiLoGatewayError && error.code === 'hi_lo_disabled' && error.status === 503) return false
  return !(error instanceof HiLoGatewayError) || error.status === 0 || error.status >= 500 || error.code === 'hi-lo-invalid-response' || error.code === 'retry_required'
}

export async function submitHiLoCommand(gateway: HiLoGateway, store: HiLoPendingOperationStore, command: HiLoPendingCommand, signal?: AbortSignal): Promise<HiLoSession> {
  store.write(command)
  try {
    const session = await executeHiLoCommand(gateway, command, signal)
    store.clear(command.operationId)
    return session
  } catch (error) {
    // Only a definitive server rejection proves that this command did not mutate state.
    if (!isHiLoRecoverableError(error)) store.clear(command.operationId)
    throw error
  }
}

/** A duplicate command can return its older original snapshot. Always refresh after replay. */
export async function recoverHiLoPendingCommand(gateway: HiLoGateway, store: HiLoPendingOperationStore, signal?: AbortSignal): Promise<HiLoSession> {
  const command = store.read()
  if (command) {
    try { await submitHiLoCommand(gateway, store, command, signal) }
    catch (error) {
      if (isHiLoRecoverableError(error)) throw error
      // A stale revision or already-completed round needs the latest authoritative state.
    }
  }
  return gateway.getSession(signal)
}

function record(value: unknown): value is Record<string, unknown> { return !!value && typeof value === 'object' && !Array.isArray(value) }
function isCommand(value: unknown): value is HiLoPendingCommand {
  if (!record(value) || typeof value.operationId !== 'string' || !value.operationId || value.operationId.length > 160) return false
  return value.kind === 'start'
    ? Number.isSafeInteger(value.wagerCents) && Number(value.wagerCents) > 0
    : value.kind === 'action' && typeof value.roundId === 'string' && value.roundId.length > 0 && value.roundId.length <= 160 && Number.isSafeInteger(value.expectedRevision) && Number(value.expectedRevision) >= 0 && ['higher', 'lower', 'skip', 'cashout'].includes(String(value.choice))
}
