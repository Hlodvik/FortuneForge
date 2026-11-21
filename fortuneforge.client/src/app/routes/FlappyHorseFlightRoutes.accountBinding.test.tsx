// @vitest-environment jsdom
import { act, useEffect, type ReactNode } from 'react'
import { createRoot, type Root } from 'react-dom/client'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import type { AccountSummary } from '../../features/account/services/accountsApi'
import type { HorseFlightGameProps } from '@fortuneforge/games-horse-flight'
import type { FlappyFreeRunPageProps } from '../../pages/games/flappy/FlappyFreeRunPage'
import { ArcadeCompetitionRequestError, type HttpArcadeCompetitionGateway } from '../../games/arcade/arcadeCompetitionApi'

const state = vi.hoisted(() => ({
  account: null as AccountSummary | null,
  loading: false,
  flappy: [] as HttpArcadeCompetitionGateway[],
  horse: [] as HttpArcadeCompetitionGateway[],
  flappyProps: [] as FlappyFreeRunPageProps[],
  horseProps: [] as HorseFlightGameProps[],
  horseRefresh: undefined as (() => void | Promise<void>) | undefined,
  flappyUnmount: vi.fn(), horseUnmount: vi.fn(),
  reload: vi.fn(),
}))
vi.mock('../../features/account/useAuthenticatedAccount', () => ({
  useAuthenticatedAccount: () => ({ account: state.account, isLoading: state.loading, error: null, reload: state.reload }),
}))
vi.mock('../../components/InGameShell', () => ({
  InGameShell: ({ account, children }: { account: AccountSummary; children: ReactNode }) => <>
    <output aria-label="Displayed balance">{account.balances.slotsCredits}</output>{children}
  </>,
}))
vi.mock('../../pages/games/flappy/FlappyFreeRunPage', () => ({
  FlappyFreeRunPage: (props: FlappyFreeRunPageProps) => {
    useEffect(() => () => state.flappyUnmount(), [])
    state.flappyProps.push(props)
    state.flappy.push(props.gateway as HttpArcadeCompetitionGateway)
    return <div>Flappy</div>
  },
}))
vi.mock('@fortuneforge/games-horse-flight', async importOriginal => ({
  ...await importOriginal<typeof import('@fortuneforge/games-horse-flight')>(),
  HorseFlightGame: (props: HorseFlightGameProps) => {
    useEffect(() => () => state.horseUnmount(), [])
    state.horseProps.push(props)
    return <div>Horse Flight{props.renderLaunchScreen?.({ available: true, connecting: false, error: null, retryConnection: () => undefined })}</div>
  },
}))
vi.mock('./horseFlightCompetitionGateway', async importOriginal => {
  const original = await importOriginal<typeof import('./horseFlightCompetitionGateway')>()
  return {
    ...original,
    HorseFlightCompetitionGatewayAdapter: class extends original.HorseFlightCompetitionGatewayAdapter {
      constructor(...args: ConstructorParameters<typeof original.HorseFlightCompetitionGatewayAdapter>) {
        super(...args)
        state.horse.push(args[0] as HttpArcadeCompetitionGateway)
        state.horseRefresh = args[3]
      }
    },
  }
})
import { AuthenticatedFlappyRoute } from './FlappyRoute'
import { AuthenticatedHorseFlightRoute } from './HorseFlightRoute'

const account = (userId: string, credits = 100): AccountSummary => ({
  userId, playerName: userId, email: `${userId}@example.test`, role: 'Player', createdAtUtc: '2026-11-01T00:00:00Z',
  balances: { slotsCredits: credits, freeGames: 0 },
  slots: { spinsPlayed: 0, wins: 0, losses: 0, creditsWagered: 0, creditsWon: 0, netCredits: 0 },
})
const admission = { expectedWindowStartsAtUtc: '2026-11-01T00:00:00.000Z', expectedWindowEndsAtUtc: '2026-11-02T00:00:00.000Z' }
const operationKey = 'flight-original-paid-key-012345'
const routes = [
  { gameId: 'flappy', Route: AuthenticatedFlappyRoute, gateways: () => state.flappy },
  { gameId: 'horse-flight', Route: AuthenticatedHorseFlightRoute, gateways: () => state.horse },
] as const
const transport = vi.fn<typeof fetch>()
let host: HTMLDivElement, root: Root

beforeEach(() => {
  vi.stubGlobal('IS_REACT_ACT_ENVIRONMENT', true)
  vi.stubGlobal('fetch', transport)
  sessionStorage.clear(); localStorage.clear()
  state.account = account('original'); state.loading = false; state.flappy = []; state.horse = []
  state.flappyProps = []; state.horseProps = []; state.horseRefresh = undefined
  state.reload.mockReset(); state.flappyUnmount.mockReset(); state.horseUnmount.mockReset()
  transport.mockReset()
  host = document.createElement('div'); document.body.append(host); root = createRoot(host)
})
afterEach(() => { act(() => root.unmount()); host.remove(); vi.unstubAllGlobals() })
function deferred<T>() {
  let resolve!: (value: T) => void
  const promise = new Promise<T>(complete => { resolve = complete })
  return { promise, resolve }
}
async function flush() { await act(async () => { for (let index = 0; index < 12; index++) await Promise.resolve() }) }
const displayedBalance = () => host.querySelector('output')?.textContent
function click(label: string) {
  const button = [...host.querySelectorAll('button')].find(item => item.textContent === label)
  if (!button) throw new Error(`Missing ${label}`)
  act(() => button.click())
}

describe.each(routes)('$gameId route account binding', ({ gameId, Route, gateways }) => {
  const render = () => act(() => root.render(<Route />))
  const start = (gateway: HttpArcadeCompetitionGateway, legacy = false) => gameId === 'flappy'
    ? gateway.startFlappyAttempt('daily', operationKey, undefined, legacy ? { recoverOnly: true } : { ...admission, recoverOnly: true })
    : gateway.startHorseFlightAttempt('daily', operationKey, undefined, legacy ? { recoverOnly: true } : { ...admission, recoverOnly: true })

  it('waits for account resolution and preserves its gateway through authenticated snapshot updates', () => {
    state.account = null; state.loading = true; render()
    expect(gateways()).toHaveLength(0); expect(transport).not.toHaveBeenCalled()
    state.account = account('original'); state.loading = false; render()
    const original = gateways().at(-1)
    state.account = account('original', 99); render()
    expect(gateways().at(-1)).toBe(original)
  })

  const selectPaidMode = () => {
    if (gameId === 'horse-flight') { click('Play Ranked · R1'); click('Daily · R1') }
  }
  const paidRefresh = () => gameId === 'flappy' ? state.flappyProps.at(-1)!.onPaidAccountRefresh! : state.horseRefresh!
  const sceneGateway = () => gameId === 'flappy' ? state.flappyProps.at(-1)!.gateway : state.horseProps.at(-1)!.gateway
  const unmountCount = () => gameId === 'flappy' ? state.flappyUnmount.mock.calls.length : state.horseUnmount.mock.calls.length

  it('refreshes after paid mutations without outer auth reload, game unmount or gateway/mode replacement', async () => {
    render(); selectPaidMode()
    const competition = gateways().at(-1), scene = sceneGateway(), unmounts = unmountCount(), refresh = paidRefresh()
    const response = deferred<Response>(); transport.mockReturnValueOnce(response.promise)
    let updating: void | Promise<void> = undefined
    act(() => { updating = refresh() })
    expect(transport.mock.calls[0]![0]).toBe('/api/accounts/me')
    expect(state.reload).not.toHaveBeenCalled(); expect(unmountCount()).toBe(unmounts)
    expect(sceneGateway()).toBe(scene); expect(displayedBalance()).toBe('100')
    await act(async () => { response.resolve(new Response(JSON.stringify(account('original', 99)))); await updating })
    expect(displayedBalance()).toBe('99'); expect(gateways().at(-1)).toBe(competition)
    expect(sceneGateway()).toBe(scene); expect(paidRefresh()).toBe(refresh); expect(unmountCount()).toBe(unmounts)
    expect(state.reload).not.toHaveBeenCalled()
    render(); expect(displayedBalance()).toBe('99')
    if (gameId === 'horse-flight') expect(state.horseProps.at(-1)!.recoveryMode).toBe('paid:daily')
  })

  it('ignores an older balance response and a delayed reply after account replacement', async () => {
    render(); selectPaidMode()
    const first = deferred<Response>(), second = deferred<Response>(), late = deferred<Response>()
    transport.mockReturnValueOnce(first.promise).mockReturnValueOnce(second.promise).mockReturnValueOnce(late.promise)
    const refresh = paidRefresh()
    act(() => { void refresh(); void refresh() })
    await act(async () => { second.resolve(new Response(JSON.stringify(account('original', 98)))); await Promise.resolve() }); await flush()
    await act(async () => { first.resolve(new Response(JSON.stringify(account('original', 99)))); await Promise.resolve() }); await flush()
    expect(displayedBalance()).toBe('98')
    act(() => { void refresh() })
    state.account = account('replacement', 200); render()
    const replacement = sceneGateway()
    await act(async () => { late.resolve(new Response(JSON.stringify(account('original', 97)))); await Promise.resolve() }); await flush()
    expect(displayedBalance()).toBe('200'); expect(sceneGateway()).toBe(replacement)
    expect(state.reload).not.toHaveBeenCalled()
  })

  it.each(['identity', 'unauthorized', 'transient'] as const)('reloads auth only when a paid balance read reveals %s', async condition => {
    render(); selectPaidMode()
    const scene = sceneGateway(), unmounts = unmountCount()
    transport.mockResolvedValueOnce(condition === 'identity'
      ? new Response(JSON.stringify(account('replacement', 200)))
      : new Response('{}', { status: condition === 'unauthorized' ? 401 : 503 }))
    await act(async () => { await paidRefresh()() }); await flush()
    expect(state.reload).toHaveBeenCalledTimes(condition === 'transient' ? 0 : 1)
    expect(displayedBalance()).toBe('100'); expect(sceneGateway()).toBe(scene); expect(unmountCount()).toBe(unmounts)
  })

  it('keeps original body, key and account across bearer/cookie fallback after account replacement', async () => {
    render(); const original = gateways().at(-1)!
    state.account = account('replacement'); render()
    const replacement = gateways().at(-1)!
    expect(replacement).not.toBe(original)
    sessionStorage.setItem('fortune-forge.account-token', 'a'.repeat(32))
    localStorage.setItem('fortune-forge.account-token', 'b'.repeat(32))
    transport.mockImplementation(async (_input, init) => new Response(JSON.stringify({ code: 'game-session-changed' }), {
      status: new Headers(init?.headers).has('Authorization') ? 401 : 403,
    }))
    await expect(start(original)).rejects.toMatchObject({ status: 403, code: 'game-session-changed' })
    expect(transport).toHaveBeenCalledTimes(3)
    expect(transport.mock.calls.map(([, init]) => new Headers(init?.headers).get('Authorization')))
      .toEqual([`Bearer ${'a'.repeat(32)}`, `Bearer ${'b'.repeat(32)}`, null])
    for (const [path, init] of transport.mock.calls) {
      expect(path).toBe(`/api/arcade-competitions/${gameId}/daily/attempts?recoverOnly=true`)
      expect(init?.body).toBe(JSON.stringify(admission))
      expect(new Headers(init?.headers).get('Idempotency-Key')).toBe(operationKey)
      expect(new Headers(init?.headers).get('X-FortuneForge-Expected-Account')).toBe('original')
    }
    transport.mockClear()
    await expect(start(replacement, true)).rejects.toBeInstanceOf(ArcadeCompetitionRequestError)
    for (const [, init] of transport.mock.calls) {
      expect(init?.body).toBeUndefined()
      expect(new Headers(init?.headers).get('Idempotency-Key')).toBe(operationKey)
      expect(new Headers(init?.headers).get('X-FortuneForge-Expected-Account')).toBe('replacement')
    }
  })

  it('binds leaderboard reads and exact terminal replay writes to the same account', async () => {
    render(); const gateway = gateways().at(-1)!
    transport.mockResolvedValueOnce(new Response(JSON.stringify({ gameId, period: 'all-time', leaderboard: [] })))
      .mockResolvedValueOnce(new Response(JSON.stringify({ code: 'game-session-changed' }), { status: 403 }))
    await gateway.getLeaderboard(gameId, 'all-time')
    const runId = `${gameId === 'flappy' ? 'flappy' : 'horse_flight'}_${'a'.repeat(64)}`
    const replay = gameId === 'flappy' ? { totalTicks: 30, flapTicks: [0, 8] } : { totalTicks: 30, jumpTicks: [0, 8], rightClickTicks: [12] }
    const completion = gameId === 'flappy'
      ? gateway.completeFlappyReplay('daily', runId, replay as { totalTicks: number; flapTicks: number[] })
      : gateway.completeHorseFlightReplay('daily', runId, replay as { totalTicks: number; jumpTicks: number[]; rightClickTicks: number[] })
    await expect(completion).rejects.toMatchObject({ status: 403, code: 'game-session-changed' })
    expect(transport.mock.calls[1]![1]?.body).toBe(JSON.stringify(replay))
    for (const [, init] of transport.mock.calls)
      expect(new Headers(init?.headers).get('X-FortuneForge-Expected-Account')).toBe('original')
  })
})

it('keeps the actual paid Horse Flight start and completion in the selected mode while balance callbacks finish', async () => {
  act(() => root.render(<AuthenticatedHorseFlightRoute />)); click('Play Ranked · R1'); click('Daily · R1')
  const scene = state.horseProps.at(-1)!.gateway!, competition = state.horse.at(-1), unmounts = state.horseUnmount.mock.calls.length
  const runId = `horse_flight_${'a'.repeat(64)}`
  const firstBalance = deferred<Response>(), secondBalance = deferred<Response>()
  let balanceReads = 0
  transport.mockImplementation(async (input) => {
    const path = String(input)
    if (path === '/api/accounts/me') return ++balanceReads === 1 ? firstBalance.promise : secondBalance.promise
    if (path.endsWith('/attempts')) return new Response(JSON.stringify({ ...admission, attemptId: 'flight-attempt', gameId: 'horse-flight', period: 'daily',
      startsAtUtc: admission.expectedWindowStartsAtUtc, endsAtUtc: admission.expectedWindowEndsAtUtc,
      entryFeeCents: 100, wasReplay: false, runId, seed: 42 }))
    if (path.endsWith('/replay')) return new Response(JSON.stringify({ runId, score: 0, phase: 'fell', wasReplay: false }))
    return new Response(JSON.stringify({ gameId: 'horse-flight', period: 'daily', startsAtUtc: admission.expectedWindowStartsAtUtc,
      endsAtUtc: admission.expectedWindowEndsAtUtc, entriesOpen: true, isCompleted: false,
      totalUniquePlayers: 0, leaderboard: [], solePlayerRefund: null, visibleJackpotCents: 0 }))
  })
  await act(async () => { await scene.start({ idempotencyKey: operationKey }) })
  expect(balanceReads).toBe(1); expect(state.reload).not.toHaveBeenCalled()
  await act(async () => { firstBalance.resolve(new Response(JSON.stringify(account('original', 99)))); await Promise.resolve() }); await flush()
  expect(displayedBalance()).toBe('99'); expect(state.horseProps.at(-1)!.gateway).toBe(scene)
  expect(await scene.getStatus()).toMatchObject({ balance: 99, recoveryMode: 'paid:daily' })
  await act(async () => { expect(await scene.complete(runId, 30, [], {}, [])).toMatchObject({ balance: 99 }) })
  await act(async () => { secondBalance.resolve(new Response(JSON.stringify(account('original', 98)))); await Promise.resolve() }); await flush()
  expect(displayedBalance()).toBe('98'); expect(state.horseProps.at(-1)!.gateway).toBe(scene)
  expect(state.horse.at(-1)).toBe(competition); expect(state.horseProps.at(-1)!.recoveryMode).toBe('paid:daily')
  expect(state.horseUnmount).toHaveBeenCalledTimes(unmounts); expect(state.reload).not.toHaveBeenCalled()
})
