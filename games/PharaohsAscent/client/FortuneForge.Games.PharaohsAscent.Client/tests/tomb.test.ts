import { describe, expect, it } from 'vitest'
import { decodeTombState, isTombAction } from '../src/tomb'
import { slotState, tombRun, tombState } from './fixtures'

describe('Sealed Tomb public contract', () => {
  it('accepts complete idle and active server snapshots without constructing choices', () => {
    const idle = slotState().gameFeatureState
    expect(decodeTombState(idle)).toBe(idle.tomb)
    const active = slotState(1, { ...tombState(1), activeRun: tombRun() }).gameFeatureState
    expect(decodeTombState(active)).toBe(active.tomb)
    expect(decodeTombState(active)?.activeRun?.choices).toBe(active.tomb.activeRun?.choices)
  })

  it('keeps legacy funding receipts readable and checks a supplied qualifying count', () => {
    const current = slotState().gameFeatureState
    expect(decodeTombState(current)?.funding.minimumScarabs).toBe(3)
    const { minimumScarabs: _minimumScarabs, ...legacyFunding } = current.tomb.funding
    expect(decodeTombState({ ...current, tomb: { ...current.tomb, funding: legacyFunding } })).not.toBeNull()
    for (const minimumScarabs of [0, -1, 1.5, 31]) {
      expect(decodeTombState({ ...current, tomb: { ...current.tomb,
        funding: { ...current.tomb.funding, minimumScarabs } } })).toBeNull()
    }
  })

  it.each([null, [], {}, { kind: 'other' }, { kind: 'pharaoh-ascent', tomb: [] }])('rejects incomplete or foreign envelopes: %j', value => {
    expect(decodeTombState(value)).toBeNull()
  })

  it.each([
    { contractVersion: 2 }, { revision: -1 }, { revision: Number.MAX_SAFE_INTEGER + 1 },
    { funding: { sigils: 6, requiredSigils: 6, averageWagerPoints: 12 } },
    { funding: { sigils: 0, requiredSigils: 0, averageWagerPoints: 0 } },
    { funding: { sigils: 0, requiredSigils: 6, averageWagerPoints: NaN } },
    { tickets: [{ id: 'same', fundedWagerPoints: 12 }, { id: 'same', fundedWagerPoints: 12 }] },
    { pendingAction: { action: 'open-room', expectedRevision: 0 } },
  ])('rejects invalid snapshot boundaries: %j', fields => {
    expect(decodeTombState({ ...slotState().gameFeatureState, tomb: { ...tombState(), ...fields } })).toBeNull()
  })

  it('rejects duplicate room identities, unknown relics and contradictory phases', () => {
    const run = tombRun()
    for (const activeRun of [
      { ...run, choices: [run.choices[0], run.choices[0], run.choices[2]] },
      { ...run, choices: [run.choices[0], { ...run.choices[1], relic: 'unknown' }, run.choices[2]] },
      { ...run, phase: 'finale' }, { ...run, torchesRemaining: 0 },
      { ...run, path: [{ step: 2, kind: 'coin', relic: null, awardPoints: 24 }] },
    ]) expect(decodeTombState({ ...slotState().gameFeatureState, tomb: { ...tombState(), activeRun } })).toBeNull()
  })

  it('retains the exact pending action and permits a complete three-key finale', () => {
    const pendingAction = { action: 'open-sarcophagus', expectedRevision: 8, choiceId: null } as const
    const state = slotState(8, { ...tombState(8), pendingAction,
      activeRun: { ...tombRun(), keys: 3, torchesRemaining: 3, phase: 'finale', choices: [], path: [1, 2, 3].map(step => ({ step, kind: 'key', relic: null, awardPoints: 12 })) } })
    expect(decodeTombState(state.gameFeatureState)?.pendingAction).toBe(pendingAction)
    expect(isTombAction(pendingAction)).toBe(true)
    expect(isTombAction({ action: 'open-room', expectedRevision: 8, choiceId: '' })).toBe(false)
    expect(isTombAction({ action: 'collect', expectedRevision: 8, choiceId: 'unrelated' })).toBe(false)
  })
})
