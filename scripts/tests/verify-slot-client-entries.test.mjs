import assert from 'node:assert/strict'
import test from 'node:test'
import { inspectSlotClientEntry } from '../verify-slot-client-entries.mjs'

test('manifest wrappers cannot count as native clients even when mounted', () => {
  const errors = inspectSlotClientEntry({ id: 'test-slot', frontend: { manifestExport: 'GAME' },
    entrySource: 'export default function Game({renderSlot}) { return renderSlot(manifest) }', registered: true })
  assert.equal(errors.length, 1)
  assert.match(errors[0], /deleted generic slot renderer/)
})

test('a native module needs an actual route integration', () => {
  const entry = { id: 'test-slot', frontend: {}, entrySource: "export { Game as default } from './Game.js'" }
  assert.match(inspectSlotClientEntry({ ...entry, registered: false })[0], /no game-owned client/)
  assert.deepEqual(inspectSlotClientEntry({ ...entry, registered: true }), [])
})

test('an embedded art build and a metadata-only entry do not establish client ownership', () => {
  assert.match(inspectSlotClientEntry({ id: 'frame', frontend: { kind: 'frame' }, entrySource: '<html></html>', registered: true })[0], /API boundary/)
  assert.match(inspectSlotClientEntry({ id: 'metadata', frontend: {}, entrySource: 'export const GAME = {}', registered: true })[0], /no default game component/)
})
