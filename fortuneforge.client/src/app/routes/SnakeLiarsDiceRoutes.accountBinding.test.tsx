// @vitest-environment jsdom
import { createGameCommandRecovery } from '@fortuneforge/client-presentation/game-command-recovery'
import type { HttpLiarsDiceGateway } from '@fortuneforge/games-liars-dice'
import type { HttpSnakeGateway } from '@fortuneforge/games-snake'
import { act, type ReactNode } from 'react'
import { createRoot, type Root } from 'react-dom/client'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import type { AccountSummary } from '../../features/account/services/accountsApi'

const state = vi.hoisted(() => ({
  account: null as AccountSummary | null,
  loading: false,
  snake: [] as HttpSnakeGateway[],
  dice: [] as HttpLiarsDiceGateway[],
}))
vi.mock('../../features/account/useAuthenticatedAccount', () => ({
  useAuthenticatedAccount: () => ({ account: state.account, isLoading: state.loading, error: null, reload: vi.fn() }),
}))
vi.mock('../../components/InGameShell', () => ({ InGameShell: ({ children }: { children: ReactNode }) => <>{children}</> }))
vi.mock('../../features/audio/GameAmbientMusic', () => ({ GameAmbientMusic: () => null }))
vi.mock('@fortuneforge/games-snake', async importOriginal => ({
  ...await importOriginal<typeof import('@fortuneforge/games-snake')>(),
  SnakeGame: ({ gateway }: { gateway: HttpSnakeGateway }) => {
    state.snake.push(gateway)
    return <div>Snake</div>
  },
}))
vi.mock('@fortuneforge/games-liars-dice', async importOriginal => ({
  ...await importOriginal<typeof import('@fortuneforge/games-liars-dice')>(),
  LiarsDiceGame: ({ gateway }: { gateway: HttpLiarsDiceGateway }) => {
    state.dice.push(gateway)
    return <div>Liar’s Dice</div>
  },
}))
import { AuthenticatedLiarsDiceRoute } from './LiarsDiceRoute'
import { AuthenticatedSnakeRoute } from './SnakeRoute'

const account = (userId: string, credits = 100): AccountSummary => ({
  userId, playerName: userId, email: `${userId}@example.test`, role: 'Player', createdAtUtc: '2026-11-01T00:00:00Z',
  balances: { slotsCredits: credits, freeGames: 0 },
  slots: { spinsPlayed: 0, wins: 0, losses: 0, creditsWagered: 0, creditsWon: 0, netCredits: 0 },
})
const routes = [
  { gameId: 'snake', Route: AuthenticatedSnakeRoute, gateways: () => state.snake, resource: 'games', body: '{}' },
  { gameId: 'liars-dice', Route: AuthenticatedLiarsDiceRoute, gateways: () => state.dice, resource: 'matches', body: '{"dicePerPlayer":1}' },
] as const
const transport = vi.fn<typeof fetch>()
let host: HTMLDivElement, root: Root

beforeEach(() => {
  vi.stubGlobal('IS_REACT_ACT_ENVIRONMENT', true)
  vi.stubGlobal('fetch', transport)
  sessionStorage.clear(); localStorage.clear()
  state.account = account('original'); state.loading = false; state.snake = []; state.dice = []
  transport.mockReset()
  host = document.createElement('div'); document.body.append(host); root = createRoot(host)
})
afterEach(() => { act(() => root.unmount()); host.remove(); vi.unstubAllGlobals() })

describe.each(routes)('$gameId route account binding', ({ gameId, Route, gateways, resource, body }) => {
  const render = () => act(() => root.render(<Route />))
  const start = (gateway: HttpSnakeGateway | HttpLiarsDiceGateway, signal?: AbortSignal) => gameId === 'snake'
    ? (gateway as HttpSnakeGateway).startGame(undefined, signal)
    : (gateway as HttpLiarsDiceGateway).startMatch({ dicePerPlayer: 1 }, signal)

  it('waits for authentication and preserves the gateway through balance refreshes', () => {
    state.account = null; state.loading = true; render()
    expect(gateways()).toHaveLength(0)
    expect(transport).not.toHaveBeenCalled()
    state.account = account('original'); state.loading = false; render()
    const original = gateways().at(-1)
    state.account = account('original', 99); render()
    expect(gateways().at(-1)).toBe(original)
    state.account = account('replacement'); render()
    expect(gateways().at(-1)).not.toBe(original)
  })

  it('retains the captured account and exact pending command through bearer and cookie fallback', async () => {
    render()
    const original = gateways().at(-1)!
    state.account = account('replacement'); render()
    const replacement = gateways().at(-1)!
    sessionStorage.setItem('fortune-forge.account-token', 'a'.repeat(32))
    localStorage.setItem('fortune-forge.account-token', 'b'.repeat(32))
    transport.mockImplementation(async (_input, init) => new Response(JSON.stringify({ code: 'game-session-changed' }), {
      status: new Headers(init?.headers).has('Authorization') ? 401 : 403,
    }))
    const signal = new AbortController().signal
    await expect(start(original, signal)).rejects.toMatchObject({ status: 403 })
    expect(transport).toHaveBeenCalledTimes(3)
    expect(transport.mock.calls.map(([, init]) => new Headers(init?.headers).get('Authorization')))
      .toEqual([`Bearer ${'a'.repeat(32)}`, `Bearer ${'b'.repeat(32)}`, null])
    const recovery = createGameCommandRecovery({ accountId: 'original', gameId, routePrefix: `/api/games/${gameId}` })
    const pending = recovery.read().pending
    expect(pending).not.toBeNull()
    for (const [path, init] of transport.mock.calls) {
      expect(path).toBe(`/api/games/${gameId}/${resource}`)
      expect(init?.body).toBe(body)
      expect(init?.signal).toBe(signal)
      expect(new Headers(init?.headers).get('Idempotency-Key')).toBe(pending!.idempotencyKey)
      expect(new Headers(init?.headers).get('X-FortuneForge-Expected-Account')).toBe('original')
    }
    transport.mockClear()
    await expect(start(replacement)).rejects.toMatchObject({ status: 403 })
    expect(transport).toHaveBeenCalledTimes(3)
    expect(recovery.read().pending).toEqual(pending)
    for (const [, init] of transport.mock.calls)
      expect(new Headers(init?.headers).get('X-FortuneForge-Expected-Account')).toBe('replacement')
  })
})
