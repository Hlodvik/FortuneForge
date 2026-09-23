import { describe, expect, it } from 'vitest'
import { FlappyRules, advanceFlappyFrame, replayFlappySimulation, startFlappySimulation } from './flappySimulation'
import { FlappyReplaySession } from './flappyReplaySession'

describe('authoritative Flappy simulation mirror', () => {
  it('matches the fixed first flap physics and deterministic no-flap terminal run', () => {
    const started = startFlappySimulation(17)
    const flapped = advanceFlappyFrame(started, true)
    const terminal = replayFlappySimulation(17, 36, [])

    expect(flapped.birdVelocity).toBe(FlappyRules.flapVelocity + FlappyRules.gravityPerTick)
    expect(flapped.birdY).toBe(started.birdY + flapped.birdVelocity)
    expect(terminal).toMatchObject({ tick: 36, score: 0, phase: 'ground-collision' })
  })

  it('records only zero-based flap instants and produces one terminal completion', () => {
    const session = new FlappyReplaySession('run-17', 17)
    session.advanceFrame(true)
    while (session.view.status === 'running') session.advanceFrame()

    const completion = session.takeCompletion()
    expect(completion?.replay.flapTicks).toEqual([0])
    expect(completion?.replay.totalTicks).toBeGreaterThan(1)
    expect(completion?.display.phase).not.toBe('playing')
    expect(session.takeCompletion()).toBeNull()
  })

  it('rejects noncanonical replay streams and frames after collision', () => {
    expect(() => replayFlappySimulation(17, 36, [3, 3])).toThrow('canonical')
    expect(() => replayFlappySimulation(17, 37, [])).toThrow('terminal collision')
  })
})
