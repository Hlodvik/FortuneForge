// @vitest-environment jsdom
import { act, cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi, type Mocked } from 'vitest'
import type { CrapsExtraBetRequest, CrapsGateway, CrapsOutcome, CrapsRound, CrapsStatus } from './contracts'
import { CrapsGame } from './CrapsGame'

const sounds = vi.hoisted(() => ({
  click: vi.fn(), roll: vi.fn(), result: vi.fn(), getMuted: vi.fn(() => false), setMuted: vi.fn(), dispose: vi.fn(),
}))

vi.mock('./crapsAudio', () => ({
  playCrapsClick: sounds.click,
  playCrapsRoll: sounds.roll,
  playCrapsResult: sounds.result,
  getCrapsMuted: sounds.getMuted,
  setCrapsMuted: sounds.setMuted,
  disposeCrapsAudio: sounds.dispose,
}))

const playerId = 'craps-visual-player'
const roundId = '00000000-0000-4000-8000-000000000051'
const status: CrapsStatus = { available: true, minimumStake: 1, maximumStake: 100, stakeIncrement: 1, mode: 'visual-test' }
const storageKey = `fortuneforge:craps:round:${encodeURIComponent(playerId)}:${encodeURIComponent(status.mode)}`

beforeEach(() => {
  vi.clearAllMocks()
  sounds.getMuted.mockReturnValue(false)
  vi.stubGlobal('matchMedia', vi.fn((query: string) => ({
    matches: query === '(prefers-reduced-motion: reduce)', media: query, onchange: null,
    addListener: vi.fn(), removeListener: vi.fn(), addEventListener: vi.fn(), removeEventListener: vi.fn(), dispatchEvent: vi.fn(),
  })))
})

afterEach(() => {
  cleanup()
  sessionStorage.clear()
  localStorage.clear()
  vi.unstubAllGlobals()
})

describe('Craps table visual interaction safety', () => {
  it('includes the table artwork by default, including when the host supplies no art prop', async () => {
    const server = fakeServer()
    render(<CrapsGame gateway={server.gateway} />)
    await readyToBet()
    const board = screen.getByRole('region', { name: 'Craps table' })
    expect(board.getAttribute('style')).toContain('craps-table-backdrop')
    expect(server.gateway.startRound).not.toHaveBeenCalled()
    expect(server.gateway.roll).not.toHaveBeenCalled()
  })

  it('uses existing chip pictures in the selector and builds an aligned image stack on the felt', async () => {
    const server = fakeServer()
    render(<CrapsGame gateway={server.gateway} />)
    await readyToBet()
    const selector = screen.getByLabelText('Select chip value')
    const buttons = selector.querySelectorAll('button')
    expect(buttons.length).toBeGreaterThanOrEqual(5)
    for (const button of buttons) {
      expect(button.querySelector('img')?.getAttribute('src')).toMatch(/chip-(?:1|2|5|10|25|50)\.png/)
      expect(button.getAttribute('aria-label')).toMatch(/^Select R\d+ chip$/)
    }
    fireEvent.click(screen.getByRole('button', { name: 'Select R2 chip' }))
    expect(screen.getByLabelText('Hand totals').textContent).toContain('R10.00')
    expect(screen.getByRole('button', { name: 'Select R2 chip' }).getAttribute('aria-pressed')).toBe('true')
    fireEvent.click(screen.getByRole('button', { name: 'Pass Line, add R2' }))
    fireEvent.click(screen.getByRole('button', { name: 'Pass Line, add R2' }))
    const stack = screen.getByLabelText('R14.00 on Pass Line')
    expect(stack.classList.contains('ff-craps-chip-stack')).toBe(true)
    const images = stack.querySelectorAll('img')
    expect(images).toHaveLength(3)
    expect(stack.style.getPropertyValue('--chip-stack-count')).toBe('3')
    expect([...images].map(image => image.style.getPropertyValue('--chip-index'))).toEqual(['0', '1', '2'])
    expect([...images].every(image => /chip-(?:10|2)\.png/.test(image.getAttribute('src') ?? ''))).toBe(true)
    expect(stack.textContent).toBe('R14.00')
    expect(server.gateway.startRound).not.toHaveBeenCalled()
  })

  it('adds every supported wager to the total and submits those exact independent amounts', async () => {
    const server = fakeServer()
    render(<CrapsGame gateway={server.gateway} />)
    await readyToBet()
    fireEvent.click(screen.getByRole('button', { name: 'Clear bets' }))
    fireEvent.click(screen.getByRole('button', { name: 'Select R2 chip' }))
    for (let index = 0; index < 3; index++) fireEvent.click(screen.getByRole('button', { name: 'Pass Line, add R2' }))
    fireEvent.click(screen.getByRole('button', { name: 'Select R5 chip' }))
    fireEvent.click(screen.getByRole('button', { name: /^Field, add R5,/ }))
    fireEvent.click(screen.getByRole('button', { name: /^Field, add R5,/ }))
    fireEvent.click(screen.getByRole('button', { name: /^Any Seven, add R5,/ }))
    fireEvent.click(screen.getByRole('button', { name: /^Any Craps, add R5,/ }))
    expect(screen.getByLabelText('Hand totals').textContent).toContain('R26.00')
    expect(screen.getByLabelText('R6.00 on Pass Line')).toBeTruthy()
    expect(screen.getByLabelText('R10.00 on Field')).toBeTruthy()
    expect(screen.getByLabelText('R5.00 on Any Seven')).toBeTruthy()
    expect(screen.getByLabelText('R5.00 on Any Craps')).toBeTruthy()
    expect(screen.queryByRole('button', { name: /Don't Pass|Hardway|Place six|Come bet/i })).toBeNull()
    fireEvent.click(screen.getByRole('button', { name: 'Bet R6 on Pass Line' }))
    await screen.findByRole('button', { name: 'Roll come-out' })
    expect(server.gateway.startRound).toHaveBeenCalledTimes(1)
    expect(server.gateway.startRound.mock.calls[0]?.[0]).toBe(6)
    expect(server.gateway.startRound.mock.calls[0]?.[2]).toEqual([
      { kind: 'field', stake: 10 }, { kind: 'any-seven', stake: 5 }, { kind: 'any-craps', stake: 5 },
    ])
    expect(screen.getByLabelText('Hand totals').textContent).toContain('R26.00')
    expect(server.gateway.roll).not.toHaveBeenCalled()
  })

  it('clears all draft wagers and restores the whole draft one change at a time with Undo', async () => {
    const server = fakeServer()
    render(<CrapsGame gateway={server.gateway} />)
    await readyToBet()
    fireEvent.click(screen.getByRole('button', { name: 'Clear bets' }))
    expect((screen.getByRole('button', { name: 'Bet R0 on Pass Line' }) as HTMLButtonElement).disabled).toBe(true)
    expect(screen.queryByLabelText(/^R[\d.]+ on Pass Line$/)).toBeNull()
    fireEvent.click(screen.getByRole('button', { name: 'Select R1 chip' }))
    fireEvent.click(screen.getByRole('button', { name: 'Pass Line, add R1' }))
    fireEvent.click(screen.getByRole('button', { name: /^Field, add R1,/ }))
    fireEvent.click(screen.getByRole('button', { name: /^Any Seven, add R1,/ }))
    expect(screen.getByLabelText('Hand totals').textContent).toContain('R3.00')
    fireEvent.click(screen.getByRole('button', { name: 'Undo last chip' }))
    expect(screen.getByLabelText('Hand totals').textContent).toContain('R2.00')
    expect(screen.queryByLabelText('R1.00 on Any Seven')).toBeNull()
    expect(screen.getByLabelText('R1.00 on Field')).toBeTruthy()
    fireEvent.click(screen.getByRole('button', { name: 'Undo last chip' }))
    expect(screen.getByLabelText('Hand totals').textContent).toContain('R1.00')
    expect(screen.queryByLabelText('R1.00 on Field')).toBeNull()
    fireEvent.click(screen.getByRole('button', { name: 'Undo last chip' }))
    expect((screen.getByRole('button', { name: 'Bet R0 on Pass Line' }) as HTMLButtonElement).disabled).toBe(true)
    fireEvent.click(screen.getByRole('button', { name: 'Undo last chip' }))
    expect(screen.getByLabelText('Hand totals').textContent).toContain('R10.00')
    expect((screen.getByRole('button', { name: 'Undo last chip' }) as HTMLButtonElement).disabled).toBe(true)
    expect(server.gateway.startRound).not.toHaveBeenCalled()
  })

  it('rejects an added chip beyond the table cap instead of clamping or submitting a different wager', async () => {
    const server = fakeServer()
    render(<CrapsGame gateway={server.gateway} />)
    await readyToBet()
    fireEvent.click(screen.getByRole('button', { name: 'Select R50 chip' }))
    fireEvent.click(screen.getByRole('button', { name: 'Pass Line, add R50' }))
    fireEvent.click(screen.getByRole('button', { name: 'Pass Line, add R50' }))
    expect(screen.getByRole('alert').textContent).toContain('Bet limit: R1.00–R100.00')
    expect(screen.getByLabelText('R60.00 on Pass Line')).toBeTruthy()
    expect(screen.getByLabelText('Hand totals').textContent).toContain('R60.00')
    fireEvent.click(screen.getByRole('button', { name: 'Bet R60 on Pass Line' }))
    await screen.findByRole('button', { name: 'Roll come-out' })
    expect(server.gateway.startRound.mock.calls[0]?.[0]).toBe(60)
    expect(screen.queryByRole('alert')).toBeNull()
  })

  it('starts a clean Undo history when editing a completed hand rather than undoing old-hand chips', async () => {
    const server = fakeServer()
    render(<CrapsGame gateway={server.gateway} />)
    await readyToBet()
    fireEvent.click(screen.getByRole('button', { name: 'Pass Line, add R5' }))
    fireEvent.click(screen.getByRole('button', { name: 'Bet R15 on Pass Line' }))
    fireEvent.click(await screen.findByRole('button', { name: 'Roll come-out' }))
    fireEvent.click(await screen.findByRole('button', { name: 'Roll point 6' }))
    await screen.findByRole('button', { name: 'Repeat R15' })
    fireEvent.click(screen.getByRole('button', { name: 'Edit bet' }))
    await screen.findByRole('button', { name: 'Bet R15 on Pass Line' })
    const undo = screen.getByRole('button', { name: 'Undo last chip' }) as HTMLButtonElement
    expect(undo.disabled).toBe(true)
    fireEvent.click(undo)
    expect(screen.getByLabelText('R15.00 on Pass Line')).toBeTruthy()
    fireEvent.click(screen.getByRole('button', { name: 'Pass Line, add R5' }))
    expect(undo.disabled).toBe(false)
    expect(screen.getByLabelText('R20.00 on Pass Line')).toBeTruthy()
    fireEvent.click(undo)
    expect(screen.getByLabelText('R15.00 on Pass Line')).toBeTruthy()
    expect(undo.disabled).toBe(true)
    expect(server.gateway.startRound).toHaveBeenCalledTimes(1)
  })

  it('blocks repeated bet clicks while the accepted bet response is pending', async () => {
    const server = fakeServer()
    const pending = deferred<CrapsRound>()
    server.gateway.startRound.mockImplementationOnce(() => pending.promise)
    render(<CrapsGame gateway={server.gateway} />)
    const bet = await readyToBet()
    fireEvent.click(bet)
    fireEvent.click(bet)
    expect(server.gateway.startRound).toHaveBeenCalledTimes(1)
    expect(bet.disabled).toBe(true)
    expect((screen.getByRole('button', { name: /^Pass Line, add R/ }) as HTMLButtonElement).disabled).toBe(true)
    expect((screen.getByRole('button', { name: 'Select R5 chip' }) as HTMLButtonElement).disabled).toBe(true)
    expect(sounds.roll).not.toHaveBeenCalled()
    await act(async () => pending.resolve(readyRound()))
    await waitFor(() => expect((screen.getByRole('button', { name: 'Roll come-out' }) as HTMLButtonElement).disabled).toBe(false))
    expect(server.gateway.roll).not.toHaveBeenCalled()
  })

  it('disables wager controls while watching another shooter', async () => {
    const server = fakeServer()
    render(<CrapsGame gateway={server.gateway} isYourTurn={false} activePlayerName="Maya" />)
    await waitFor(() => expect(server.gateway.getStatus).toHaveBeenCalledTimes(1))
    await waitFor(() => expect(screen.getByText('Maya is the shooter')).toBeTruthy())
    fireEvent.click(screen.getByRole('button', { name: 'Bets' }))
    expect((screen.getByLabelText('Pass Line bet in Rand') as HTMLInputElement).disabled).toBe(true)
    const waiting = screen.getByRole('button', { name: 'Waiting' }) as HTMLButtonElement
    expect(waiting.disabled).toBe(true)
    fireEvent.click(waiting)
    expect(server.gateway.startRound).not.toHaveBeenCalled()
    expect(server.gateway.roll).not.toHaveBeenCalled()
    expect(sounds.roll).not.toHaveBeenCalled()
  })

  it('shows the established point and permits odds once, without changing the Pass Line stake', async () => {
    const server = fakeServer()
    restore(server, pointRound())
    render(<CrapsGame gateway={server.gateway} playerId={playerId} />)
    const addOdds = await screen.findByRole('button', { name: 'Add odds' }) as HTMLButtonElement
    await waitFor(() => expect(addOdds.disabled).toBe(false))
    expect(screen.getByLabelText('Point 6 is on').textContent).toContain('ON')
    fireEvent.click(addOdds)
    const odds = screen.getByRole('dialog', { name: 'Pass Line odds' })
    expect(odds.textContent).toContain('Pays 6:5')
    fireEvent.change(screen.getByLabelText('Pass Line odds stake'), { target: { value: '1' } })
    fireEvent.click(screen.getByRole('button', { name: 'Place odds' }))
    await waitFor(() => expect(screen.queryByRole('button', { name: 'Add odds' })).toBeNull())
    expect(server.gateway.placeOdds).toHaveBeenCalledTimes(1)
    expect(server.gateway.placeOdds.mock.calls[0]?.slice(0, 2)).toEqual([roundId, 1])
    expect(server.state.stake).toBe(10)
    expect(screen.getByLabelText('Hand totals').textContent).toContain('R11.00')
    expect(server.gateway.startRound).not.toHaveBeenCalled()
    expect(server.gateway.roll).not.toHaveBeenCalled()
  })

  it('does not offer odds on the come-out or if the gateway does not support them', async () => {
    const server = fakeServer()
    restore(server, readyRound())
    const first = render(<CrapsGame gateway={server.gateway} playerId={playerId} />)
    await screen.findByRole('button', { name: 'Roll come-out' })
    expect(screen.queryByRole('button', { name: 'Add odds' })).toBeNull()
    first.unmount()
    server.state = pointRound()
    const { placeOdds: _odds, ...legacy } = server.gateway
    render(<CrapsGame gateway={legacy} playerId={playerId} />)
    await screen.findByRole('button', { name: 'Roll point 6' })
    expect(screen.queryByRole('button', { name: 'Add odds' })).toBeNull()
    expect(server.gateway.placeOdds).not.toHaveBeenCalled()
  })

  it('plays button and throw sounds from interactions, then a single result cue after settlement', async () => {
    const server = fakeServer()
    render(<CrapsGame gateway={server.gateway} />)
    await readyToBet()
    expect(sounds.click).not.toHaveBeenCalled()
    expect(sounds.roll).not.toHaveBeenCalled()
    expect(sounds.result).not.toHaveBeenCalled()
    const chipButton = screen.getByRole('button', { name: 'Select R2 chip' })
    fireEvent.click(chipButton.querySelector('img')!)
    expect(sounds.click).toHaveBeenCalledTimes(1)
    fireEvent.click(screen.getByRole('button', { name: 'Bet R10 on Pass Line' }))
    const comeOut = await screen.findByRole('button', { name: 'Roll come-out' })
    expect(sounds.roll).not.toHaveBeenCalled()
    fireEvent.click(comeOut)
    await screen.findByRole('button', { name: 'Roll point 6' })
    expect(sounds.roll).toHaveBeenCalledTimes(1)
    expect(sounds.result).not.toHaveBeenCalled()
    fireEvent.click(screen.getByRole('button', { name: 'Roll point 6' }))
    await screen.findByRole('button', { name: 'Repeat R10' })
    expect(sounds.roll).toHaveBeenCalledTimes(2)
    await waitFor(() => expect(sounds.result).toHaveBeenCalledExactlyOnceWith(true))
    fireEvent.click(screen.getByRole('button', { name: 'Bets' }))
    expect(sounds.result).toHaveBeenCalledTimes(1)
    expect(screen.getByLabelText('Hand totals').textContent).toContain('R20.00')
  })

  it('restores a resolved hand without replaying a throw or result and exposes a persistent mute control', async () => {
    const server = fakeServer()
    restore(server, advance(pointRound(), { first: 2, second: 4, total: 6, result: 'point-hit', isTerminal: true, totalReturn: 20 }))
    const view = render(<CrapsGame gateway={server.gateway} playerId={playerId} />)
    await screen.findByRole('button', { name: 'Repeat R10' })
    expect(sounds.roll).not.toHaveBeenCalled()
    expect(sounds.result).not.toHaveBeenCalled()
    expect(server.gateway.startRound).not.toHaveBeenCalled()
    expect(server.gateway.roll).not.toHaveBeenCalled()
    fireEvent.click(screen.getByRole('button', { name: 'Mute Craps sounds' }))
    expect(sounds.setMuted).toHaveBeenLastCalledWith(true)
    expect(screen.getByRole('button', { name: 'Unmute Craps sounds' }).getAttribute('aria-pressed')).toBe('true')
    fireEvent.click(screen.getByRole('button', { name: 'Unmute Craps sounds' }))
    expect(sounds.setMuted).toHaveBeenLastCalledWith(false)
    view.unmount()
    expect(sounds.dispose).toHaveBeenCalledTimes(1)
  })

  it('locks an uncertain hand and uses Check again only to read the authoritative hand', async () => {
    const server = fakeServer()
    restore(server, readyRound())
    render(<CrapsGame gateway={server.gateway} playerId={playerId} />)
    const roll = await screen.findByRole('button', { name: 'Roll come-out' }) as HTMLButtonElement
    await waitFor(() => expect(roll.disabled).toBe(false))
    server.gateway.roll.mockRejectedValueOnce(new TypeError('Response lost'))
    server.gateway.getRound.mockRejectedValueOnce(new TypeError('Still offline'))
    fireEvent.click(roll)
    const retry = await screen.findByRole('button', { name: 'Check again' })
    expect(roll.disabled).toBe(true)
    fireEvent.click(roll)
    expect(server.gateway.roll).toHaveBeenCalledTimes(1)
    expect(server.gateway.startRound).not.toHaveBeenCalled()
    server.state = pointRound()
    fireEvent.click(retry)
    await waitFor(() => expect((screen.getByRole('button', { name: 'Roll point 6' }) as HTMLButtonElement).disabled).toBe(false))
    expect(server.gateway.getRound).toHaveBeenCalledTimes(3)
    expect(server.gateway.roll).toHaveBeenCalledTimes(1)
    expect(server.gateway.startRound).not.toHaveBeenCalled()
    expect(server.gateway.placeOdds).not.toHaveBeenCalled()
  })
})

async function readyToBet(): Promise<HTMLButtonElement> {
  const button = await screen.findByRole('button', { name: /^Bet R/ }) as HTMLButtonElement
  await waitFor(() => expect(button.disabled).toBe(false))
  return button
}

function restore(server: ReturnType<typeof fakeServer>, round: CrapsRound) {
  server.state = round
  sessionStorage.setItem(storageKey, round.roundId)
}

function readyRound(): CrapsRound {
  return { roundId, stake: 10, phase: 'come-out', point: null, rolls: [], lastOutcome: null, extraBets: [] }
}

function pointRound(): CrapsRound {
  return advance(readyRound(), { first: 3, second: 3, total: 6, result: 'point-established', isTerminal: false, totalReturn: null })
}

function advance(round: CrapsRound, outcome: CrapsOutcome): CrapsRound {
  return {
    ...round,
    phase: outcome.isTerminal ? 'resolved' : 'point',
    point: outcome.result === 'point-established' ? outcome.total : round.point,
    rolls: [...round.rolls, { rollNumber: round.rolls.length + 1, first: outcome.first, second: outcome.second, total: outcome.total, result: outcome.result }],
    lastOutcome: outcome,
  }
}

function deferred<T>() {
  let resolve!: (value: T) => void
  const promise = new Promise<T>(done => { resolve = done })
  return { promise, resolve }
}

function fakeServer(tableStatus: CrapsStatus = status) {
  const server: { state: CrapsRound; gateway: Mocked<Required<CrapsGateway>> } = {
    state: readyRound(), gateway: {} as Mocked<Required<CrapsGateway>>,
  }
  server.gateway = {
    getStatus: vi.fn(async () => ({ ...tableStatus })),
    getRound: vi.fn(async () => server.state),
    startRound: vi.fn(async (stake: number, _signal?: AbortSignal, extras?: readonly CrapsExtraBetRequest[]) => {
      server.state = { ...readyRound(), stake, extraBets: (extras ?? []).map(bet => ({ ...bet, resolved: false, won: false, totalReturn: null })) }
      return server.state
    }),
    roll: vi.fn(async () => {
      server.state = advance(server.state, server.state.phase === 'come-out'
        ? { first: 3, second: 3, total: 6, result: 'point-established', isTerminal: false, totalReturn: null }
        : { first: 2, second: 4, total: 6, result: 'point-hit', isTerminal: true, totalReturn: server.state.stake * 2 })
      return server.state
    }),
    placeOdds: vi.fn(async (_id: string, stake: number) => {
      server.state = { ...server.state, extraBets: [...server.state.extraBets ?? [], { kind: 'odds', stake, resolved: false, won: false, totalReturn: null }] }
      return server.state
    }),
  }
  return server
}
