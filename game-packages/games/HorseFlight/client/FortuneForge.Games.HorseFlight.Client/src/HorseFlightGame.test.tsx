// @vitest-environment jsdom
import { act, cleanup, fireEvent, render, screen } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { HorseFlightGame } from './HorseFlightGame'
import { HorseFlightGatewayError, type HorseFlightGateway } from './contracts'

afterEach(() => {
  vi.useRealTimers()
  cleanup()
})

describe('HorseFlightGame', () => {
  it('does not misrepresent a deliberately unavailable run as a connection failure', async () => {
    const gateway: HorseFlightGateway = {
      getStatus: () => Promise.reject(new HorseFlightGatewayError('Not ready.', 'horse-flight-disabled', 503)),
      start: () => Promise.reject(new Error('Not called.')),
      complete: () => Promise.reject(new Error('Not called.')),
    }

    render(<HorseFlightGame gateway={gateway} />)

    expect((await screen.findByRole('alert')).textContent).toContain('temporarily unavailable')
    expect(screen.queryByRole('button', { name: 'Retry connection' })).toBeNull()
    expect((screen.getByRole('button', { name: 'Run unavailable' }) as HTMLButtonElement).disabled).toBe(true)
  })

  it('keeps cycling the running sheet after the opening rear completes', async () => {
    vi.useFakeTimers()
    const gateway: HorseFlightGateway = {
      getStatus: () => Promise.resolve({ available: true, tickMilliseconds: 100_000, balance: 10_000, mode: 'recorded-free-play' }),
      start: () => Promise.resolve({ runId: 'running-sprite', seed: 42, tickMilliseconds: 100_000 }),
      complete: () => Promise.reject(new Error('The controlled test run should not complete.')),
    }

    const { container } = render(<HorseFlightGame gateway={gateway} />)
    await act(async () => { await Promise.resolve() })
    fireEvent.click(screen.getByRole('button', { name: 'Start run' }))
    await act(async () => { await Promise.resolve() })
    act(() => { vi.advanceTimersByTime(1_250) })

    const frame = () => container.querySelector('.ff-horse-flight__horse-art')?.getAttribute('data-sprite-frame')
    expect(frame()).toBe('0')

    act(() => { vi.advanceTimersByTime(90) })
    expect(frame()).toBe('1')
  })

  it('keeps the rearing sprite the same display scale and ground line as the running sprite', async () => {
    vi.useFakeTimers()
    const gateway: HorseFlightGateway = {
      getStatus: () => Promise.resolve({ available: true, tickMilliseconds: 100_000, balance: 10_000, mode: 'recorded-free-play' }),
      start: () => Promise.resolve({ runId: 'rear-scale', seed: 42, tickMilliseconds: 100_000 }),
      complete: () => Promise.reject(new Error('The controlled test run should not complete.')),
    }

    const { container } = render(<HorseFlightGame gateway={gateway} />)
    await act(async () => { await Promise.resolve() })
    fireEvent.click(screen.getByRole('button', { name: 'Start run' }))
    await act(async () => { await Promise.resolve() })

    const rearing = container.querySelector('.ff-horse-flight__horse-art')!
    expect(rearing.getAttribute('width')).toBe('55')
    expect(rearing.getAttribute('height')).toBe('45')
    expect(Number(rearing.getAttribute('y')) + Number(rearing.getAttribute('height'))).toBe(461)

    act(() => { vi.advanceTimersByTime(1_250) })
    const running = container.querySelector('.ff-horse-flight__horse-art')!
    expect(running.getAttribute('width')).toBe('55')
    expect(running.getAttribute('height')).toBe('45')
    expect(Number(running.getAttribute('y')) + Number(running.getAttribute('height'))).toBe(461)
  })

  it('explains an obstacle collision and displays its terminal score', async () => {
    vi.useFakeTimers()
    const gateway: HorseFlightGateway = {
      getStatus: () => Promise.resolve({ available: true, tickMilliseconds: 1, balance: 10_000, mode: 'recorded-free-play' }),
      start: () => Promise.resolve({ runId: 'collision-message', seed: 42, tickMilliseconds: 1 }),
      complete: () => Promise.reject(new Error('The controlled test run is not being recorded.')),
    }

    render(<HorseFlightGame gateway={gateway} />)
    await act(async () => { await Promise.resolve() })
    fireEvent.click(screen.getByRole('button', { name: 'Start run' }))
    await act(async () => { await Promise.resolve() })
    act(() => { vi.advanceTimersByTime(1_250) })
    await act(async () => { await Promise.resolve() })
    act(() => { vi.advanceTimersByTime(4_000) })

    expect(screen.getByText('Run ended')).toBeTruthy()
    expect(screen.getByText('You hit an obstacle.')).toBeTruthy()
    expect(screen.getByText(/Score \d+/)).toBeTruthy()
  })

  it('keeps haunted tombstones out of the opening mountain pass', async () => {
    vi.useFakeTimers()
    const gateway: HorseFlightGateway = {
      getStatus: () => Promise.resolve({ available: true, tickMilliseconds: 100_000, balance: 10_000, mode: 'recorded-free-play' }),
      start: () => Promise.resolve({ runId: 'mountain-obstacles', seed: 42, tickMilliseconds: 100_000 }),
      complete: () => Promise.reject(new Error('The controlled test run should not complete.')),
    }

    const { container } = render(<HorseFlightGame gateway={gateway} />)
    await act(async () => { await Promise.resolve() })
    fireEvent.click(screen.getByRole('button', { name: 'Start run' }))
    await act(async () => { await Promise.resolve() })

    expect(container.querySelector('.ff-horse-flight')?.getAttribute('data-active-biome')).toBe('mountain')
    expect(container.querySelector('[data-artwork="tombstone"]')).toBeNull()
  })

  it('uses the dash sheet while right-click is held', async () => {
    vi.useFakeTimers()
    const gateway: HorseFlightGateway = {
      getStatus: () => Promise.resolve({ available: true, tickMilliseconds: 10, balance: 10_000, mode: 'recorded-free-play' }),
      start: () => Promise.resolve({ runId: 'held-dash', seed: 42, tickMilliseconds: 10 }),
      complete: () => Promise.reject(new Error('The controlled test run should not complete.')),
    }
    const { container } = render(<HorseFlightGame gateway={gateway} />)
    await act(async () => { await Promise.resolve() })
    fireEvent.click(screen.getByRole('button', { name: 'Start run' }))
    await act(async () => { await Promise.resolve() })
    act(() => { vi.advanceTimersByTime(1_250) })
    const field = screen.getByRole('region', { name: 'Horse Flight playfield' })
    fireEvent.mouseDown(field, { button: 2 })
    act(() => { vi.advanceTimersByTime(20) })

    expect(container.querySelector('.ff-horse-flight__horse-art image')?.getAttribute('href')).toContain('horse-flight-dash-sprite-sheet-v1')
  })

  it('saves the terminal leaderboard score without retaining or restarting the run', async () => {
    vi.useFakeTimers()
    let startCount = 0
    const start = vi.fn(() => Promise.resolve({ runId: `auto-${++startCount}`, seed: 42, tickMilliseconds: 1 }))
    const gateway: HorseFlightGateway = {
      getStatus: () => Promise.resolve({ available: true, tickMilliseconds: 1, balance: 10_000, mode: 'recorded-free-play' }),
      start,
      complete: () => Promise.resolve({ runId: 'auto', score: 123, balance: 10_000, phase: 'obstacle-collision', totalTicks: 1, jumpTicks: [] }),
    }
    render(<HorseFlightGame gateway={gateway} />)
    await act(async () => { await Promise.resolve() })
    fireEvent.click(screen.getByRole('button', { name: 'Start run' }))
    await act(async () => { await Promise.resolve() })
    act(() => { vi.advanceTimersByTime(1_250) })
    await act(async () => { await Promise.resolve() })
    act(() => { vi.advanceTimersByTime(4_000) })
    await act(async () => { await Promise.resolve() })
    await act(async () => { await Promise.resolve() })

    expect(start).toHaveBeenCalledTimes(1)
    expect(screen.getByRole('button', { name: 'Start fresh run' })).toBeTruthy()
  })
})
