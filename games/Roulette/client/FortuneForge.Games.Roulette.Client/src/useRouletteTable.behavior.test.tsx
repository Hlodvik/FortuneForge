// @vitest-environment jsdom
import { act, cleanup, render, renderHook, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, describe, expect, it, vi, type Mocked } from 'vitest'
import { RouletteGame } from './RouletteGame'
import { RouletteGatewayError, type RouletteBet, type RouletteGateway, type RouletteRound, type RouletteStatus } from './contracts'
import { makeBet, neighborPockets, type RouletteBetRequest } from './roulettePresentation'
import { useRouletteTable } from './useRouletteTable'

const player = 'roulette-player'
const status: RouletteStatus = { available: true, minimumStake: 1, maximumStake: 100, stakeIncrement: 1, startingBalance: 1_000, mode: 'free-play-single-zero' }
const id = '00000000-0000-4000-8000-000000000001'
const secondId = '00000000-0000-4000-8000-000000000002'
const key = (account = player, mode = status.mode, part = 'round') => `fortuneforge:roulette:${part}:${encodeURIComponent(account)}:${encodeURIComponent(mode)}`
const baseRound = (roundId = id): RouletteRound => ({ roundId, balance: 1_000, phase: 'open', bets: [], winningPocket: null, settlements: [] })
const clone = <T,>(value: T): T => JSON.parse(JSON.stringify(value)) as T

afterEach(() => { cleanup(); sessionStorage.clear(); localStorage.clear(); vi.useRealTimers() })

describe('Roulette controller recovery', () => {
  it('reconciles an accepted chip after a lost response without replaying the POST', async () => {
    const server = fakeServer()
    server.loseResponse('add', 1)
    const { result } = renderHook(() => useRouletteTable(server.gateway, player))
    await waitFor(() => expect(result.current.busy).toBe(false))
    act(() => result.current.open())
    await waitFor(() => expect(result.current.round?.phase).toBe('open'))
    act(() => result.current.add([makeBet('straight', [17], 5)]))
    await waitFor(() => expect(result.current.busy).toBe(false))
    expect(server.gateway.placeBet).toHaveBeenCalledTimes(1)
    expect(server.gateway.getRound).toHaveBeenCalledTimes(1)
    expect(result.current.round?.bets).toHaveLength(1)
    expect(result.current.round?.balance).toBe(995)
    expect(result.current.error).toContain('Table refreshed')
  })

  it('does not repeat an uncertain indexed removal after the remaining chip is renumbered', async () => {
    const server = fakeServer()
    server.state = { ...baseRound(), balance: 990, bets: [bet(0, 17, 5), bet(1, 23, 5)] }
    sessionStorage.setItem(key(), id)
    const { result } = renderHook(() => useRouletteTable(server.gateway, player))
    await waitFor(() => expect(result.current.busy).toBe(false))
    server.loseResponse('remove', 1)
    act(() => result.current.remove(0))
    await waitFor(() => expect(result.current.busy).toBe(false))
    expect(server.gateway.removeBet).toHaveBeenCalledTimes(1)
    expect(result.current.round?.bets.map(item => [item.betIndex, item.number])).toEqual([[0, 23]])
    expect(result.current.round?.balance).toBe(995)
  })

  it('stops a partially accepted neighbor batch and shows every committed chip after reconciliation', async () => {
    const server = fakeServer()
    server.state = baseRound()
    sessionStorage.setItem(key(), id)
    server.loseResponse('add', 2)
    const { result } = renderHook(() => useRouletteTable(server.gateway, player))
    await waitFor(() => expect(result.current.busy).toBe(false))
    const neighbors = neighborPockets(17, 1)
    act(() => result.current.add(neighbors.map(number => makeBet('straight', [number], 5))))
    await waitFor(() => expect(result.current.busy).toBe(false))
    expect(server.gateway.placeBet).toHaveBeenCalledTimes(2)
    expect(result.current.round?.bets.map(item => item.number)).toEqual(neighbors.slice(0, 2))
    expect(result.current.round?.balance).toBe(990)
  })

  it('retains partial double progress without doubling the original chips again', async () => {
    const server = fakeServer()
    server.state = { ...baseRound(), balance: 990, bets: [bet(0, 17, 5), bet(1, 23, 5)] }
    sessionStorage.setItem(key(), id)
    server.loseResponse('add', 2)
    const { result } = renderHook(() => useRouletteTable(server.gateway, player))
    await waitFor(() => expect(result.current.busy).toBe(false))
    const original = result.current.round!.bets
    act(() => result.current.add(original))
    await waitFor(() => expect(result.current.busy).toBe(false))
    expect(server.gateway.placeBet).toHaveBeenCalledTimes(2)
    expect(result.current.round?.bets).toHaveLength(4)
    expect(result.current.round?.balance).toBe(980)
  })

  it('blocks writes when read reconciliation fails and retries only status/GET', async () => {
    const server = fakeServer()
    server.state = baseRound()
    sessionStorage.setItem(key(), id)
    const { result } = renderHook(() => useRouletteTable(server.gateway, player))
    await waitFor(() => expect(result.current.busy).toBe(false))
    server.loseResponse('add', 1)
    server.readError = new TypeError('Still offline')
    act(() => result.current.add([makeBet('straight', [17], 5)]))
    await waitFor(() => expect(result.current.recovery).toBe('failed'))
    act(() => { result.current.add([makeBet('straight', [23], 5)]); result.current.remove(0); result.current.clear(); result.current.spin(); result.current.open() })
    expect(server.gateway.placeBet).toHaveBeenCalledTimes(1)
    expect(server.gateway.removeBet).not.toHaveBeenCalled()
    expect(server.gateway.clearBets).not.toHaveBeenCalled()
    expect(server.gateway.spin).not.toHaveBeenCalled()
    expect(server.gateway.openRound).not.toHaveBeenCalled()
    server.readError = null
    act(() => result.current.retry())
    await waitFor(() => expect(result.current.recovery).toBe('ready'))
    expect(result.current.round?.bets).toHaveLength(1)
    expect(server.gateway.placeBet).toHaveBeenCalledTimes(1)
  })

  it('clears an expired recorded table on 404 and permits a fresh table', async () => {
    const server = fakeServer()
    server.readError = new RouletteGatewayError('Expired', 'roulette-round-not-found', 404)
    sessionStorage.setItem(key(), id)
    const { result } = renderHook(() => useRouletteTable(server.gateway, player))
    await waitFor(() => expect(result.current.busy).toBe(false))
    expect(result.current.round).toBeNull()
    expect(result.current.recovery).toBe('ready')
    expect(sessionStorage.getItem(key())).toBeNull()
    expect(result.current.error).toContain('expired')
    act(() => result.current.open())
    await waitFor(() => expect(result.current.round?.phase).toBe('open'))
    expect(server.gateway.openRound).toHaveBeenCalledTimes(1)
  })

  it('keeps restoration blocked after a network failure and restores the same round when retried', async () => {
    const server = fakeServer()
    server.state = { ...baseRound(), balance: 995, bets: [bet(0, 17, 5)] }
    server.readError = new TypeError('Offline')
    sessionStorage.setItem(key(), id)
    const { result } = renderHook(() => useRouletteTable(server.gateway, player))
    await waitFor(() => expect(result.current.recovery).toBe('failed'))
    act(() => result.current.open())
    expect(server.gateway.openRound).not.toHaveBeenCalled()
    server.readError = null
    act(() => result.current.retry())
    await waitFor(() => expect(result.current.busy).toBe(false))
    expect(result.current.recovery).toBe('ready')
    expect(result.current.round?.bets).toHaveLength(1)
    expect(server.gateway.getRound).toHaveBeenCalledTimes(2)
    expect(server.gateway.openRound).not.toHaveBeenCalled()
  })

  it('blocks same-tick repeated input before React publishes disabled buttons', async () => {
    const server = fakeServer()
    const { result } = renderHook(() => useRouletteTable(server.gateway, player))
    await waitFor(() => expect(result.current.busy).toBe(false))
    act(() => { result.current.open(); result.current.open() })
    await waitFor(() => expect(result.current.busy).toBe(false))
    expect(server.gateway.openRound).toHaveBeenCalledTimes(1)
    act(() => { result.current.add([makeBet('straight', [17], 5)]); result.current.add([makeBet('straight', [17], 5)]) })
    await waitFor(() => expect(result.current.busy).toBe(false))
    expect(server.gateway.placeBet).toHaveBeenCalledTimes(1)
  })

  it('recovers a settled spin after a lost response and records its result once', async () => {
    const server = fakeServer()
    server.state = { ...baseRound(), balance: 990, bets: [bet(0, 17, 10)] }
    sessionStorage.setItem(key(), id)
    server.loseResponse('spin', 1)
    const { result } = renderHook(() => useRouletteTable(server.gateway, player))
    await waitFor(() => expect(result.current.busy).toBe(false))
    act(() => { result.current.spin(); result.current.spin() })
    await waitFor(() => expect(result.current.busy).toBe(false), { timeout: 3_000 })
    expect(server.gateway.spin).toHaveBeenCalledTimes(1)
    expect(result.current.round?.phase).toBe('settled')
    expect(result.current.round?.balance).toBe(1_350)
    expect(result.current.history).toEqual([{ roundId: id, pocket: 17 }])
    act(() => result.current.retry())
    await waitFor(() => expect(result.current.busy).toBe(false))
    expect(result.current.history).toHaveLength(1)
    expect(server.gateway.spin).toHaveBeenCalledTimes(1)
  })

  it('scopes table and deduplicated history to the account and mode on an identity change', async () => {
    const server = fakeServer()
    server.state = settledRound()
    sessionStorage.setItem(key(), id)
    localStorage.setItem(key(player, status.mode, 'history'), JSON.stringify([{ roundId: id, pocket: 17 }]))
    localStorage.setItem(key('other-player', status.mode, 'history'), JSON.stringify([{ roundId: secondId, pocket: 23 }]))
    const { result, rerender } = renderHook(({ playerId }) => useRouletteTable(server.gateway, playerId), { initialProps: { playerId: player } })
    await waitFor(() => expect(result.current.busy).toBe(false))
    expect(result.current.history).toEqual([{ roundId: id, pocket: 17 }])
    rerender({ playerId: 'other-player' })
    await waitFor(() => expect(result.current.busy).toBe(false))
    expect(result.current.round).toBeNull()
    expect(result.current.history).toEqual([{ roundId: secondId, pocket: 23 }])
    expect(sessionStorage.getItem(key('other-player'))).toBeNull()
    expect(server.gateway.getRound).toHaveBeenCalledTimes(1)
  })

  it('ignores an old open response after account change even when the gateway ignores abort', async () => {
    const server = fakeServer()
    const pending = deferred<RouletteRound>()
    server.gateway.openRound.mockImplementationOnce(() => pending.promise)
    const { result, rerender } = renderHook(({ playerId }) => useRouletteTable(server.gateway, playerId), { initialProps: { playerId: player } })
    await waitFor(() => expect(result.current.busy).toBe(false))
    act(() => result.current.open())
    rerender({ playerId: 'other-player' })
    await waitFor(() => expect(result.current.busy).toBe(false))
    await act(async () => pending.resolve(baseRound()))
    expect(result.current.round).toBeNull()
    expect(sessionStorage.getItem(key('other-player'))).toBeNull()
    expect(result.current.history).toEqual([])
  })

  it('does not carry an old round into a changed table mode when status is refreshed', async () => {
    const server = fakeServer()
    server.state = { ...baseRound(), balance: 995, bets: [bet(0, 17, 5)] }
    sessionStorage.setItem(key(), id)
    const { result } = renderHook(() => useRouletteTable(server.gateway, player))
    await waitFor(() => expect(result.current.busy).toBe(false))
    server.gateway.getStatus.mockResolvedValue({ ...status, mode: 'another-practice-mode' })
    act(() => result.current.retry())
    await waitFor(() => expect(result.current.busy).toBe(false))
    expect(result.current.round).toBeNull()
    expect(result.current.history).toEqual([])
    expect(sessionStorage.getItem(key(player, 'another-practice-mode'))).toBeNull()
    expect(server.gateway.getRound).toHaveBeenCalledTimes(1)
  })

  it('reports a failed new-table request instead of silently restoring the unrelated old settled table', async () => {
    const server = fakeServer()
    server.state = settledRound()
    sessionStorage.setItem(key(), id)
    const { result } = renderHook(() => useRouletteTable(server.gateway, player))
    await waitFor(() => expect(result.current.busy).toBe(false))
    server.gateway.openRound.mockRejectedValueOnce(new TypeError('Offline'))
    act(() => result.current.open())
    await waitFor(() => expect(result.current.busy).toBe(false))
    expect(result.current.error).toBeTruthy()
    expect(server.gateway.getRound).toHaveBeenCalledTimes(1)
    expect(server.gateway.openRound).toHaveBeenCalledTimes(1)
  })
})

describe('Roulette component controls', () => {
  it('disables doubling restored chips that no longer meet current table limits', async () => {
    const server = fakeServer({ ...status, maximumStake: 20 })
    server.state = { ...baseRound(), balance: 975, bets: [bet(0, 17, 25)] }
    sessionStorage.setItem(key(), id)
    render(<RouletteGame gateway={server.gateway} playerId={player} />)
    await waitFor(() => expect((screen.getByRole('button', { name: 'Add chip' }) as HTMLButtonElement).disabled).toBe(false))
    expect((screen.getByRole('button', { name: 'Double' }) as HTMLButtonElement).disabled).toBe(true)
    expect(server.gateway.placeBet).not.toHaveBeenCalled()
  })

  it('offers only legal chip limits and prevents nonadjacent inside selections from posting', async () => {
    const user = userEvent.setup()
    const server = fakeServer({ ...status, minimumStake: 3, maximumStake: 20, stakeIncrement: 2 })
    render(<RouletteGame gateway={server.gateway} playerId={player} />)
    const chip = await screen.findByRole('combobox', { name: 'Chip' }) as HTMLSelectElement
    await waitFor(() => expect(chip.disabled).toBe(false))
    expect(chip.value).toBe('3')
    expect(Array.from(chip.options).map(option => option.value)).toEqual(['3', '5', '19'])
    await user.click(screen.getByRole('button', { name: 'Open table' }))
    await screen.findByRole('button', { name: 'Add chip' })
    await user.selectOptions(screen.getByRole('combobox', { name: 'Bet type' }), 'split')
    await user.click(screen.getByRole('button', { name: 'Pocket 3, red' }))
    await user.click(screen.getByRole('button', { name: 'Pocket 4, black' }))
    expect((screen.getByRole('button', { name: 'Add chip' }) as HTMLButtonElement).disabled).toBe(true)
    expect(server.gateway.placeBet).not.toHaveBeenCalled()
    await user.click(screen.getByRole('button', { name: 'Pocket 4, black' }))
    await user.click(screen.getByRole('button', { name: 'Pocket 2, black' }))
    await user.click(screen.getByRole('button', { name: 'Add chip' }))
    await waitFor(() => expect(server.gateway.placeBet).toHaveBeenCalledTimes(1))
    expect(server.gateway.placeBet.mock.calls[0]?.[1]).toEqual({ kind: 'split', stake: 3, number: null, numbers: [2, 3] })
  })

  it('keeps primary controls disabled until a failed table restoration is read successfully', async () => {
    const user = userEvent.setup()
    const server = fakeServer()
    server.readError = new TypeError('Offline')
    sessionStorage.setItem(key(), id)
    render(<RouletteGame gateway={server.gateway} playerId={player} />)
    const retry = await screen.findByRole('button', { name: 'Retry restoration' })
    expect((screen.getByRole('button', { name: 'Open table' }) as HTMLButtonElement).disabled).toBe(true)
    expect((screen.getByRole('combobox', { name: 'Chip' }) as HTMLSelectElement).disabled).toBe(true)
    server.readError = null
    server.state = { ...baseRound(), balance: 995, bets: [bet(0, 17, 5)] }
    await user.click(retry)
    const add = await screen.findByRole('button', { name: 'Add chip' }) as HTMLButtonElement
    await waitFor(() => expect(add.disabled).toBe(false))
    expect(server.gateway.openRound).not.toHaveBeenCalled()
    expect(server.gateway.placeBet).not.toHaveBeenCalled()
  })
})

function bet(betIndex: number, number: number, stake: number): RouletteBet { return { ...makeBet('straight', [number], stake), betIndex, playerId: player } }
function settledRound(): RouletteRound { return { ...baseRound(), phase: 'settled', balance: 1_350, bets: [bet(0, 17, 10)], winningPocket: 17, settlements: [{ playerId: player, kind: 'straight', stake: 10, won: true, totalReturn: 360 }] } }
function deferred<T>() { let resolve!: (value: T) => void; const promise = new Promise<T>(done => { resolve = done }); return { promise, resolve } }

function fakeServer(tableStatus: RouletteStatus = status) {
  type Operation = 'add' | 'remove' | 'clear' | 'spin'
  const counts: Record<Operation, number> = { add: 0, remove: 0, clear: 0, spin: 0 }
  let lost: { operation: Operation; call: number } | null = null
  const server: { state: RouletteRound; readError: Error | null; loseResponse: (operation: Operation, call: number) => void; gateway: Mocked<RouletteGateway> } = {
    state: baseRound(),
    readError: null as Error | null,
    loseResponse(operation: Operation, call: number) { lost = { operation, call } },
    gateway: {} as Mocked<RouletteGateway>,
  }
  function answer(operation: Operation) {
    counts[operation]++
    if (lost?.operation === operation && lost.call === counts[operation]) throw new TypeError('Response lost after commit')
    return clone(server.state)
  }
  function gatewayMethods(): Mocked<RouletteGateway> {
    return {
      getStatus: vi.fn(async (_signal?: AbortSignal) => clone(tableStatus)),
      getRound: vi.fn(async (_roundId: string, _signal?: AbortSignal) => { if (server.readError) throw server.readError; return clone(server.state) }),
      openRound: vi.fn(async (_signal?: AbortSignal) => { server.state = baseRound(); return clone(server.state) }),
      placeBet: vi.fn(async (_roundId: string, request: RouletteBetRequest, _signal?: AbortSignal) => {
        server.state = { ...server.state, balance: server.state.balance - request.stake, bets: [...server.state.bets, { ...request, playerId: player, betIndex: server.state.bets.length }] }
        return answer('add')
      }),
      removeBet: vi.fn(async (_roundId: string, index: number, _signal?: AbortSignal) => {
        const removed = server.state.bets[index]!
        server.state = { ...server.state, balance: server.state.balance + removed.stake, bets: server.state.bets.filter((_, position) => position !== index).map((item, betIndex) => ({ ...item, betIndex })) }
        return answer('remove')
      }),
      clearBets: vi.fn(async (_roundId: string, _signal?: AbortSignal) => {
        server.state = { ...server.state, balance: server.state.balance + server.state.bets.reduce((sum, item) => sum + item.stake, 0), bets: [] }
        return answer('clear')
      }),
      spin: vi.fn(async (_roundId: string, _signal?: AbortSignal) => { server.state = settledRound(); return answer('spin') }),
    } satisfies RouletteGateway
  }
  server.gateway = gatewayMethods()
  return server
}
