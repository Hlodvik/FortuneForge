import { describe, expect, it } from 'vitest'
import { AsteroidsReplaySession, maximumReplayCommands, maximumReplaySteps } from './asteroidsReplaySession'

const runId = 'asteroids_0123456789abcdef'
const seedHex = '000000000000002a'

describe('Asteroids replay session', () => {
  it('records canonical persistent masks, omits the implicit None, and gives the latest turn priority', () => {
    const session = new AsteroidsReplaySession(runId, seedHex)
    session.advanceFrame()
    session.setHeld('left', true)
    session.advanceFrame()
    session.setHeld('right', true)
    session.setHeld('left', true)
    session.advanceFrame()
    session.setHeld('right', false)
    session.advanceFrame()
    session.clearHeld()
    session.advanceFrame()

    const completion = finish(session)
    expect(completion.replay.commands).toEqual([
      { step: 1, input: 2 },
      { step: 2, input: 4 },
      { step: 3, input: 2 },
      { step: 4, input: 0 },
    ])
    expect(Object.keys(completion.replay).sort()).toEqual(['commands', 'totalSteps'])
    expect(JSON.stringify(completion.replay)).not.toMatch(/seed|score|user|time|run/i)
  })

  it('finishes once at game over or the exact time cap and cannot mutate afterwards', () => {
    const session = new AsteroidsReplaySession(runId, seedHex)
    while (session.view.status === 'running') session.advanceFrame()
    const completion = session.takeCompletion()
    const frozen = session.view

    expect(completion).not.toBeNull()
    expect(completion!.replay.totalSteps).toBeLessThanOrEqual(maximumReplaySteps)
    expect(session.takeCompletion()).toBeNull()
    session.setHeld('fire', true)
    session.advanceFrame()
    expect(session.view).toEqual(frozen)
  })

  it('uses the exact 3,600-frame cap when the ship survives', () => {
    const session = new AsteroidsReplaySession(runId, seedHex)
    session.setHeld('right', true)
    session.setHeld('thrust', true)
    for (let frame = 0; frame < maximumReplaySteps; frame++) session.advanceFrame()

    const completion = session.takeCompletion()
    expect(session.view.status).toBe('finished')
    expect(completion).toMatchObject({ replay: { totalSteps: maximumReplaySteps }, display: { reason: 'time-up' } })
  })

  it('fails visibly before recording a 513th transition', () => {
    const session = new AsteroidsReplaySession(runId, seedHex)
    for (let index = 0; index < maximumReplayCommands; index++) {
      session.setHeld('fire', index % 2 === 0)
      session.advanceFrame()
    }
    session.setHeld('fire', true)
    session.advanceFrame()

    expect(session.view.status).toBe('failed')
    expect(session.view.error).toMatch(/transition limit/i)
    expect(session.takeCompletion()).toBeNull()
  })
})

function finish(session: AsteroidsReplaySession) {
  while (session.view.status === 'running') session.advanceFrame()
  return session.takeCompletion()!
}
