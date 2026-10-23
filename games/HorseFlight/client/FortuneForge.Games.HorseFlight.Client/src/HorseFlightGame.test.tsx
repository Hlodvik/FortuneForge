// @vitest-environment jsdom
import { act, cleanup, fireEvent, render, screen } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { HorseFlightGame, groundImpactHorsePosition, horseFlightTerminalMessage, horseFlightTerminalReaction, skidHorsePosition, terminalHorseSpriteFor, tripHorsePosition } from './HorseFlightGame'
import { HorseFlightGatewayError, type HorseFlightGateway } from './contracts'
import { horseScreenX, startHorse, stepHorse, type ObstacleKind } from './horseFlightSimulation'

afterEach(() => {
  vi.useRealTimers()
  cleanup()
})

describe('HorseFlightGame', () => {
  it('lets the host render its launch actions inside the game scene without the old heading strip', async () => {
    const gateway: HorseFlightGateway = {
      getStatus: () => Promise.resolve({ available: true, tickMilliseconds: 20, balance: 10_000, mode: 'recorded-free-play' }),
      start: () => Promise.reject(new Error('Not called.')),
      complete: () => Promise.reject(new Error('Not called.')),
    }

    const { container } = render(<HorseFlightGame
      gateway={gateway}
      renderLaunchScreen={({ available }) => <div>
        <h1>HORSE FLIGHT</h1>
        <p>Ready to run?</p>
        <button disabled={!available} type="button">Play Free</button>
      </div>}
    />)

    const play = await screen.findByRole('button', { name: 'Play Free' })
    expect((play as HTMLButtonElement).disabled).toBe(false)
    expect(play.closest('.ff-horse-flight__lobby-scene')).not.toBeNull()
    expect(container.querySelector('.ff-horse-flight__header')).toBeNull()
    expect(screen.queryByText('Mountain pass')).toBeNull()
  })

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
    const openingFrame = () => container.querySelector('.ff-horse-flight__horse-art')?.getAttribute('data-sprite-frame')
    expect(openingFrame()).toBe('0')
    for (let expected = 1; expected < 12; expected += 1) {
      act(() => { vi.advanceTimersByTime(90) })
      expect(openingFrame()).toBe(String(expected))
    }
    const rear = container.querySelector('.ff-horse-flight__horse-art')!
    expect(rear.querySelector('g[clip-path]')?.getAttribute('clip-path')).toBe('url(#horse-flight-rear-frame)')
    expect(rear.querySelector('g[clip-path] image')?.getAttribute('x')).toBe('-724')
    expect(rear.querySelector('g[clip-path] image')?.getAttribute('y')).toBe('-1086')
    expect(rear.querySelector('g[transform]')?.getAttribute('transform')).toBe('translate(-5 66)')

    act(() => { vi.advanceTimersByTime(90) })

    const frame = () => container.querySelector('.ff-horse-flight__horse-art')?.getAttribute('data-sprite-frame')
    const frameViewBox = () => container.querySelector('.ff-horse-flight__horse-art')?.getAttribute('viewBox')
    const frameImageX = () => container.querySelector('.ff-horse-flight__horse-art image')?.getAttribute('x')
    const frameImageY = () => container.querySelector('.ff-horse-flight__horse-art image')?.getAttribute('y')
    expect(frame()).toBe('0')
    expect(frameViewBox()).toBe('0 0 444 300')
    expect(frameImageX()).toBe('0')
    expect(frameImageY()).toBe('-108')
    expect(container.querySelector('#horse-flight-running-frame rect')?.getAttribute('width')).toBe('444')
    expect(container.querySelector('#horse-flight-running-frame rect')?.getAttribute('height')).toBe('300')
    expect(container.querySelector('.ff-horse-flight__horse-art image')?.getAttribute('href')).toContain('horse-flight-run-sprite-sheet-v3')

    act(() => { vi.advanceTimersByTime(90) })
    expect(frame()).toBe('1')
    expect(frameViewBox()).toBe('0 0 443 300')
    expect(frameImageX()).toBe('-444')
    expect(frameImageY()).toBe('-108')

    act(() => { vi.advanceTimersByTime(270) })
    expect(frame()).toBe('4')
    expect(frameViewBox()).toBe('0 0 444 300')
    expect(frameImageX()).toBe('0')
    expect(frameImageY()).toBe('-494')
  })

  it('matches the running horse to the approved rearing size on the same ground line', async () => {
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
    const foreground = container.querySelector('[data-scenery-layer="foreground"]')!
    const horseActor = container.querySelector('.ff-horse-flight__horse-actor')!
    expect(foreground.compareDocumentPosition(horseActor) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy()
    expect(rearing.getAttribute('width')).toBe('126')
    expect(rearing.getAttribute('height')).toBe('126')
    expect(Number(rearing.getAttribute('y')) + Number(rearing.getAttribute('height'))).toBe(461)

    act(() => { vi.advanceTimersByTime(1_080) })
    const running = container.querySelector('.ff-horse-flight__horse-art')!
    expect(running.getAttribute('width')).toBe('141')
    expect(running.getAttribute('height')).toBe('95')
    expect(Number(running.getAttribute('y')) + Number(running.getAttribute('height'))).toBe(461)
    expect(running.getAttribute('image-rendering')).toBe('pixelated')
    expect(rearing.getAttribute('image-rendering')).toBe('pixelated')
  })

  it('scrolls midground scenery only while the run is actively moving', async () => {
    vi.useFakeTimers()
    const gateway: HorseFlightGateway = {
      getStatus: () => Promise.resolve({ available: true, tickMilliseconds: 100_000, balance: 10_000, mode: 'recorded-free-play' }),
      start: () => Promise.resolve({ runId: 'scene-motion', seed: 42, tickMilliseconds: 100_000 }),
      complete: () => Promise.reject(new Error('The controlled test run should not complete.')),
    }

    render(<HorseFlightGame gateway={gateway} />)
    await act(async () => { await Promise.resolve() })
    fireEvent.click(screen.getByRole('button', { name: 'Start run' }))
    await act(async () => { await Promise.resolve() })

    const field = screen.getByRole('region', { name: 'Horse Flight playfield' })
    expect(field.getAttribute('data-scene-moving')).toBe('false')
    act(() => { vi.advanceTimersByTime(1_080) })
    expect(field.getAttribute('data-scene-moving')).toBe('true')
    fireEvent.click(screen.getByRole('button', { name: 'Pause' }))
    expect(field.getAttribute('data-scene-moving')).toBe('false')
  })

  it('explains an obstacle collision and displays its terminal score', async () => {
    vi.useFakeTimers()
    const gateway: HorseFlightGateway = {
      getStatus: () => Promise.resolve({ available: true, tickMilliseconds: 1, balance: 10_000, mode: 'recorded-free-play' }),
      start: () => Promise.resolve({ runId: 'collision-message', seed: 42, tickMilliseconds: 1 }),
      complete: () => Promise.reject(new Error('The controlled test run is not being recorded.')),
    }

    const { container } = render(<HorseFlightGame gateway={gateway} />)
    await act(async () => { await Promise.resolve() })
    fireEvent.click(screen.getByRole('button', { name: 'Start run' }))
    await act(async () => { await Promise.resolve() })
    act(() => { vi.advanceTimersByTime(1_250) })
    await act(async () => { await Promise.resolve() })
    act(() => { vi.advanceTimersByTime(4_000) })

    expect(screen.getByText('A peasant caught you with a lasso!')).toBeTruthy()
    expect(screen.getByText(/Score \d+/)).toBeTruthy()
    expect(screen.queryByText('Run ended')).toBeNull()
    expect(screen.queryByText('You hit an obstacle.')).toBeNull()
    expect(container.querySelector('[data-obstacle-kind="lasso-thrower"][data-attack-stage="caught"]')).not.toBeNull()
    expect(container.querySelector('[data-obstacle-kind="lasso-thrower"] .ff-horse-flight__capture-throw-art[data-capture-phase="caught"] image')?.getAttribute('href')).toContain('horse-flight-lasso-miss-sheet-v1')
    expect(container.querySelector('[data-terminal-horse-sprite="lasso-caught"] image')?.getAttribute('href')).toContain('horse-flight-lasso-capture-sheet-v1')
    expect(container.querySelectorAll('.ff-horse-flight__horse-actor .ff-horse-flight__horse-art')).toHaveLength(1)
    expect(container.querySelector('.ff-horse-flight__lasso-travel')).toBeNull()
    expect(container.querySelector('[class*="rider"]')).toBeNull()
  })

  it('shows the tower net wrapped around the caught horse', async () => {
    vi.useFakeTimers()
    const gateway: HorseFlightGateway = {
      getStatus: () => Promise.resolve({ available: true, tickMilliseconds: 1, balance: 10_000, mode: 'recorded-free-play' }),
      start: () => Promise.resolve({ runId: 'net-capture', seed: 46, tickMilliseconds: 1 }),
      complete: () => Promise.reject(new Error('The controlled test run is not being recorded.')),
    }

    const { container } = render(<HorseFlightGame gateway={gateway} />)
    await act(async () => { await Promise.resolve() })
    fireEvent.click(screen.getByRole('button', { name: 'Start run' }))
    await act(async () => { await Promise.resolve() })
    act(() => { vi.advanceTimersByTime(1_250) })
    await act(async () => { await Promise.resolve() })
    act(() => { vi.advanceTimersByTime(4_000) })

    expect(screen.getByText('You were caught in the tower’s net!')).toBeTruthy()
    expect(container.querySelector('[data-obstacle-kind="net-tower"] .ff-horse-flight__capture-throw-art[data-capture-phase="caught"] image')?.getAttribute('href')).toContain('horse-flight-net-miss-sheet-v1')
    expect(container.querySelector('.ff-horse-flight__horse-actor[data-terminal-reaction="net-caught"]')).not.toBeNull()
    expect(container.querySelector('[data-terminal-horse-sprite="net-caught"] image')?.getAttribute('href')).toContain('horse-flight-net-capture-sheet-v1')
    expect(container.querySelectorAll('.ff-horse-flight__horse-actor .ff-horse-flight__horse-art')).toHaveLength(1)
    expect(container.querySelector('.ff-horse-flight__net-travel')).toBeNull()
    expect(container.querySelector('.ff-horse-flight__net-tower-thrower')).toBeNull()
  })

  it('passes the terminal obstacle outcome and score to a host end screen', async () => {
    vi.useFakeTimers()
    const gateway: HorseFlightGateway = {
      getStatus: () => Promise.resolve({ available: true, tickMilliseconds: 1, balance: 10_000, mode: 'recorded-free-play' }),
      start: () => Promise.resolve({ runId: 'custom-collision-message', seed: 42, tickMilliseconds: 1 }),
      complete: () => Promise.reject(new Error('The controlled test run is not being recorded.')),
    }

    render(<HorseFlightGame
      gateway={gateway}
      renderEndScreen={({ phase, score, terminalMessage, terminalObstacleKind }) => <div>
        <span>{terminalMessage}</span>
        <span>{phase}:{terminalObstacleKind}:{score}</span>
      </div>}
    />)
    await act(async () => { await Promise.resolve() })
    fireEvent.click(screen.getByRole('button', { name: 'Start run' }))
    await act(async () => { await Promise.resolve() })
    act(() => { vi.advanceTimersByTime(1_250) })
    await act(async () => { await Promise.resolve() })
    act(() => { vi.advanceTimersByTime(4_000) })
    await act(async () => { await Promise.resolve() })

    expect(screen.getByText('A peasant caught you with a lasso!')).toBeTruthy()
    expect(screen.getByText('obstacle-collision:lasso-thrower:48')).toBeTruthy()
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

  it('uses right-click for speed only while local free-play god mode is on', async () => {
    vi.useFakeTimers()
    const complete = vi.fn(() => Promise.reject(new Error('A debugged run must not be submitted.')))
    const gateway: HorseFlightGateway = {
      getStatus: () => Promise.resolve({ available: true, tickMilliseconds: 1, balance: 10_000, mode: 'free' }),
      start: () => Promise.resolve({ runId: 'free-debug', seed: 42, tickMilliseconds: 1 }),
      complete,
    }
    const { container } = render(<HorseFlightGame debugFreePlay gateway={gateway} />)
    await act(async () => { await Promise.resolve() })
    fireEvent.click(screen.getByRole('button', { name: 'Start run' }))
    await act(async () => { await Promise.resolve() })
    act(() => { vi.advanceTimersByTime(1_080) })
    const field = screen.getByRole('region', { name: 'Horse Flight playfield' })

    fireEvent.mouseDown(field, { button: 2 })
    act(() => { vi.advanceTimersByTime(2) })
    expect(container.querySelector('.ff-horse-flight__horse-art image')?.getAttribute('href')).toContain('horse-flight-dash-sprite-sheet-v1')
    fireEvent.mouseUp(field, { button: 2 })

    fireEvent.keyDown(window, { code: 'KeyG', ctrlKey: true, altKey: true })
    expect(screen.getByText('God mode')).toBeTruthy()
    expect(field.getAttribute('data-debug-god-mode')).toBe('true')
    fireEvent.mouseDown(field, { button: 2 })
    expect(screen.getByText('God mode · 3×')).toBeTruthy()
    fireEvent.pointerUp(window, { button: 2, buttons: 0 })
    expect(screen.queryByText('God mode · 3×')).toBeNull()
    fireEvent.mouseDown(field, { button: 2 })
    expect(screen.getByText('God mode · 3×')).toBeTruthy()
    fireEvent.blur(window)
    expect(screen.queryByText('God mode · 3×')).toBeNull()
    fireEvent.mouseDown(field, { button: 2 })
    expect(screen.getByText('God mode · 3×')).toBeTruthy()
    fireEvent.keyDown(field, { code: 'KeyP' })
    expect(screen.queryByText('God mode · 3×')).toBeNull()
    fireEvent.keyDown(field, { code: 'KeyP' })
    expect(screen.getByText('God mode')).toBeTruthy()

    fireEvent.keyDown(window, { code: 'KeyG', ctrlKey: true, altKey: true })
    expect(screen.queryByText('God mode')).toBeNull()
    act(() => { vi.advanceTimersByTime(4_000) })
    await act(async () => { await Promise.resolve() })
    expect(complete).not.toHaveBeenCalled()
    expect(screen.getByRole('button', { name: 'Start fresh run' })).toBeTruthy()
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
    expect(screen.queryByText('Score saved to the leaderboard.')).toBeNull()
  })

  it('maps terminal obstacle kinds to their distinct visual reactions', () => {
    expect(horseFlightTerminalReaction('obstacle-collision', 'fence')).toBe('trip-over')
    expect(horseFlightTerminalReaction('obstacle-collision', 'fallen-log')).toBe('trip-over')
    expect(horseFlightTerminalReaction('obstacle-collision', 'oil-spill')).toBe('slip')
    expect(horseFlightTerminalReaction('obstacle-collision', 'boulder', true)).toBe('rock-skid')
    expect(horseFlightTerminalReaction('obstacle-collision', 'boulder', false)).toBe('impact')
    expect(horseFlightTerminalReaction('obstacle-collision', 'bat-flock')).toBe('bat-retreat')
    expect(horseFlightTerminalReaction('obstacle-collision', 'lasso-thrower')).toBe('lasso-caught')
    expect(horseFlightTerminalReaction('obstacle-collision', 'net-tower')).toBe('net-caught')
    expect(horseFlightTerminalReaction('fell', null)).toBe('fall')
    expect(horseFlightTerminalReaction('running', 'fence')).toBeNull()
  })

  it.each([
    ['fence', 1, 'You tripped over a fence!'],
    ['fallen-log', 28, 'You tripped over a fallen log!'],
  ] as const)('uses the forward-tumble atlas for a %s impact', async (_kind, seed, message) => {
    vi.useFakeTimers()
    const gateway: HorseFlightGateway = {
      getStatus: () => Promise.resolve({ available: true, tickMilliseconds: 1, balance: 10_000, mode: 'recorded-free-play' }),
      start: () => Promise.resolve({ runId: `collision-atlas-${seed}`, seed, tickMilliseconds: 1 }),
      complete: () => Promise.reject(new Error('The controlled test run is not being recorded.')),
    }

    const { container } = render(<HorseFlightGame gateway={gateway} />)
    await act(async () => { await Promise.resolve() })
    fireEvent.click(screen.getByRole('button', { name: 'Start run' }))
    await act(async () => { await Promise.resolve() })
    act(() => { vi.advanceTimersByTime(1_250) })
    await act(async () => { await Promise.resolve() })
    act(() => { vi.advanceTimersByTime(500) })
    await act(async () => { await Promise.resolve() })

    const reactionArt = container.querySelector('.ff-horse-flight__horse-reaction-art')
    expect(screen.getByText(message)).toBeTruthy()
    expect(reactionArt?.getAttribute('data-terminal-horse-sprite')).toBe('trip-over')
    expect(reactionArt?.querySelector('image')?.getAttribute('href')).toContain('horse-flight-trip-over-sprite-sheet-v1')
    expect(reactionArt?.getAttribute('data-sprite-frame')).toBe('0')
    expect(reactionArt?.getAttribute('viewBox')).toBe('0 0 444 444')
    expect(reactionArt?.getAttribute('width')).toBe('141')
    expect(reactionArt?.getAttribute('height')).toBe('141')
    expect(reactionArt?.getAttribute('image-rendering')).toBe('pixelated')
    expect(reactionArt?.querySelector('g[clip-path]')?.getAttribute('clip-path')).toBe('url(#horse-flight-terminal-frame)')
    expect(reactionArt?.querySelector('clipPath rect')?.getAttribute('width')).toBe('444')
    const startingX = Number(reactionArt?.getAttribute('x'))
    const startingY = Number(reactionArt?.getAttribute('y'))
    const collisionObstacle = Array.from(container.querySelectorAll(`[data-obstacle-kind="${_kind}"]`))
      .map(element => ({
        x: Number(element.getAttribute('transform')?.match(/translate\(([-\d.]+)/)?.[1]),
        y: Number(element.getAttribute('transform')?.match(/translate\([-\d.]+ ([-\d.]+)/)?.[1]),
        width: Number(element.querySelector('svg')?.getAttribute('width')),
      }))
      .sort((left, right) => Math.abs(left.x - 336) - Math.abs(right.x - 336))[0]
    expect(collisionObstacle).toBeDefined()

    act(() => { vi.advanceTimersByTime(108) })
    expect(reactionArt?.getAttribute('data-sprite-frame')).toBe('2')
    expect(Number(reactionArt?.getAttribute('y')) + 141).toBeLessThan(collisionObstacle.y)

    act(() => { vi.advanceTimersByTime(108) })
    expect(reactionArt?.getAttribute('data-sprite-frame')).toBe('4')
    expect(Number(reactionArt?.getAttribute('y'))).toBeLessThan(startingY - 60)

    act(() => { vi.advanceTimersByTime(204) })
    expect(reactionArt?.getAttribute('data-sprite-frame')).toBe('7')
    expect(Number(reactionArt?.getAttribute('x'))).toBeGreaterThan(collisionObstacle.x + collisionObstacle.width + 8)
    expect(Number(reactionArt?.getAttribute('x'))).toBeGreaterThan(startingX + 100)
    expect(Number(reactionArt?.getAttribute('y'))).toBe(startingY)
    expect(reactionArt?.querySelector('g[transform]')?.getAttribute('transform')).toBe('translate(0 69)')
    expect(reactionArt?.querySelector('clipPath rect')?.getAttribute('y')).toBe('220')
    expect(reactionArt?.querySelector('g[clip-path] image')?.getAttribute('x')).toBe('-1331')
    expect(reactionArt?.querySelector('g[clip-path] image')?.getAttribute('y')).toBe('-444')
  })

  it.each([
    ['fence', 84, 42],
    ['fallen-log', 80, 40],
  ] as const)('lands on the platform after an airborne %s trip', (kind, width, height) => {
    const source = startHorse(29)
    const platform = source.platforms.find(item => item.id === source.standing)!
    const airborne = {
      ...source,
      horseY: platform.y - 26,
      velocity: 4,
      grounded: false,
      standing: null,
      obstacles: [{
        id: 1, biome: 'mountain' as const, kind, platformId: platform.id,
        x: horseScreenX - 10, width, height, elevation: 0, additionalSpeed: 0,
      }],
      nextObstacle: 2,
    }
    const impact = stepHorse(airborne, false).state
    expect(impact.phase).toBe('obstacle-collision')
    expect(impact.terminalObstacleKind).toBe(kind)
    expect(impact.horseY).toBeLessThan(platform.y)

    const opening = tripHorsePosition(impact, horseScreenX, 0)
    const clearing = tripHorsePosition(impact, horseScreenX, 2)
    const landing = tripHorsePosition(impact, horseScreenX, 7)
    const obstacle = impact.obstacles.find(item => item.kind === kind)!
    expect(opening.y + 140).toBeCloseTo(impact.horseY)
    expect(clearing.y + 141).toBeLessThan(platform.y - height)
    expect(landing.x).toBeGreaterThan(obstacle.x + obstacle.width)
    expect(landing.y + 140).toBeCloseTo(platform.y)
  })

  it.each(['boulder', 'market-cart', 'oil-spill'] as const)('lands an airborne %s impact on the struck platform', kind => {
    const source = startHorse(29)
    const struckPlatform = { id: 99, x: 0, width: 960, y: 412, cleared: false }
    const decoy = { id: 2, biome: 'mountain' as const, kind, platformId: source.platforms[0].id,
      x: horseScreenX - 5, width: 54, height: 36, elevation: 0, additionalSpeed: 0 }
    const struck = { ...decoy, id: 1, platformId: struckPlatform.id, x: horseScreenX + 45 }
    const impact = {
      ...source,
      horseY: 350,
      phase: 'obstacle-collision' as const,
      terminalObstacleKind: kind,
      terminalObstacleId: struck.id,
      platforms: [...source.platforms, struckPlatform],
      obstacles: [decoy, struck],
    }
    expect(groundImpactHorsePosition(impact, horseScreenX, 0).y + 140).toBeCloseTo(impact.horseY)
    expect(groundImpactHorsePosition(impact, horseScreenX, 7).y + 140).toBeCloseTo(struckPlatform.y)
  })

  it('slides a horse forward and down to the ground after landing on a rock', () => {
    const source = startHorse(29)
    const platform = source.platforms.find(item => item.id === source.standing)!
    const boulder = { id: 1, biome: 'mountain' as const, kind: 'boulder' as const,
      platformId: platform.id, x: horseScreenX - 10, width: 54, height: 36, elevation: 0, additionalSpeed: 0 }
    const impact = stepHorse({
      ...source, horseY: platform.y - boulder.height + 4, velocity: 2,
      grounded: false, standing: null, obstacles: [boulder], nextObstacle: 2,
    }, false).state
    expect(impact.terminalRockLanding).toBe(true)
    const first = skidHorsePosition(impact, horseScreenX, 0)
    const middle = skidHorsePosition(impact, horseScreenX, 4)
    const last = skidHorsePosition(impact, horseScreenX, 7)
    const struck = impact.obstacles.find(item => item.id === impact.terminalObstacleId)!
    expect(first.y + 140).toBeCloseTo(impact.horseY)
    expect(middle.x).toBeGreaterThan(first.x)
    expect(last.x).toBeGreaterThan(struck.x + struck.width)
    expect(last.y + 140).toBeCloseTo(platform.y)
    expect(terminalHorseSpriteFor('rock-skid', 0)?.sheet).toContain('horse-flight-rock-skid-sprite-sheet-v1')
    expect(terminalHorseSpriteFor('rock-skid', 7)).toMatchObject({ index: 7, groundOffset: 70 })
  })

  it('uses distinct forward trip and oil-slip atlases, with composite capture art elsewhere', () => {
    const trip = terminalHorseSpriteFor(horseFlightTerminalReaction('obstacle-collision', 'fence'), 7)
    const fallenLog = terminalHorseSpriteFor(horseFlightTerminalReaction('obstacle-collision', 'fallen-log'), 7)
    const slip = terminalHorseSpriteFor(horseFlightTerminalReaction('obstacle-collision', 'oil-spill'), 7)
    const lassoCaught = terminalHorseSpriteFor(horseFlightTerminalReaction('obstacle-collision', 'lasso-thrower'), 7)
    const netCaught = terminalHorseSpriteFor(horseFlightTerminalReaction('obstacle-collision', 'net-tower'), 7)

    expect(trip?.sheet).toContain('horse-flight-trip-over-sprite-sheet-v1')
    expect(fallenLog?.sheet).toBe(trip?.sheet)
    expect(slip?.sheet).toContain('horse-flight-slip-sprite-sheet-v2')
    expect(slip?.sheet).not.toBe(trip?.sheet)
    expect(slip).toMatchObject({ index: 7, sourceX: 1331, sourceY: 444, sourceWidth: 443, sourceHeight: 443, groundOffset: 43 })
    expect(lassoCaught).toBeNull()
    expect(netCaught).toBeNull()
    expect(terminalHorseSpriteFor('impact', 7)?.sheet).toContain('horse-flight-crash-sprite-sheet-v2')
  })

  it('describes terminal outcomes using the exact obstacle kind', () => {
    const messages = {
      crate: 'You ran into a tombstone!',
      'crate-cluster': 'You crashed into a cluster of tombstones!',
      pit: 'You fell into a sunken pit!',
      well: 'You ran into a well!',
      fence: 'You tripped over a fence!',
      carriage: 'A carriage blocked your escape!',
      'oil-spill': 'You slipped on an oil slick!',
      boulder: 'You ran into a boulder!',
      'fallen-log': 'You tripped over a fallen log!',
      dog: 'A skeleton hound caught you!',
      'bat-flock': 'A flock of bats scared you back!',
      'lasso-thrower': 'A peasant caught you with a lasso!',
      'net-tower': 'You were caught in the tower’s net!',
      skeleton: 'A skeleton blocked your escape!',
      'rolling-barrel': 'A rolling barrel knocked you down!',
      'market-cart': 'You crashed into a market cart!',
      'hanging-sign': 'You struck a hanging sign!',
      'thorn-bramble': 'You were caught in thorn brambles!',
      'spike-barricade': 'You hit a spike barricade!',
      cactus: 'You ran into a cactus!',
      'storm-cloud': 'A storm cloud struck you!',
    } satisfies Record<ObstacleKind, string>

    for (const [kind, message] of Object.entries(messages)) {
      expect(horseFlightTerminalMessage('obstacle-collision', kind as ObstacleKind)).toBe(message)
    }
    expect(horseFlightTerminalMessage('fell', null)).toBe('You missed the trail and fell!')
  })
})
