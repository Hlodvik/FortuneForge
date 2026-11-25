import assert from 'node:assert/strict'
import test from 'node:test'
import { inspectSlotClientEntry } from '../verify-slot-client-entries.mjs'
import { verifySlotClientEntries } from '../verify-slot-client-entries.mjs'
import { verifyPaidFrameBoundary } from '../verify-paid-frame-boundary.mjs'
import { verifyGameMaintenanceBoundary } from '../verify-game-maintenance.mjs'
import { mkdtempSync, mkdirSync, writeFileSync, rmSync } from 'node:fs'
import { tmpdir } from 'node:os'
import { resolve, dirname, sep } from 'node:path'
import { fileURLToPath } from 'node:url'

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

test('the full inventory distinguishes active entry integrations from disabled maintenance rather than dropping games', () => {
  const root = fileURLToPath(new URL('../../', import.meta.url))
  const report = verifySlotClientEntries(root)
  assert.deepEqual(report.errors, [])
  assert.equal(report.clients.length, report.games)
  assert.equal(report.clients.filter(client => client.status === 'maintenance').length, 11)
  assert.equal(report.clients.find(client => client.id === 'pirates-fortune').status, 'native-entry')
  assert.equal(report.clients.find(client => client.id === 'gods-of-olympus').status, 'maintenance')
  assert.equal(report.clients.find(client => client.id === 'jade-dragon').status, 'paid-frame-entry')
})

test('frame API file presence is insufficient: the real HTML entry must reach it and the host must be neutral', () => {
  const root = mkdtempSync(resolve(tmpdir(), 'ff-frame-boundary-test-'))
  const clientRoot = resolve(root, 'client')
  const write = (path, text) => { const target = resolve(root, path); mkdirSync(dirname(target), { recursive: true }); writeFileSync(target, text) }
  const input = { root, clientRoot, entry: resolve(clientRoot, 'index.html'), apiEntry: 'api.ts', hostEntry: 'host.ts' }
  try {
    write('client/index.html', '<script type="module" src="/main.js"></script>')
    write('client/main.js', 'export const decorativeOnly = true')
    write('client/api.ts', 'export function gameClient(authenticatedFetch) { return authenticatedFetch }')
    write('host.ts', 'attachAuthenticatedFrameTransport(ports)')
    assert.match(verifyPaidFrameBoundary(input).errors.join(' '), /does not reach/)
    write('client/main.js', "import { gameClient } from './api.ts'; gameClient(ports)")
    assert.deepEqual(verifyPaidFrameBoundary(input).errors, [])
    write('host.ts', 'attachAuthenticatedFrameTransport(ports); const wagerPoints = 20')
    assert.match(verifyPaidFrameBoundary(input).errors.join(' '), /without game commands/)
    assert.ok(verifyGameMaintenanceBoundary(root, 'missing-policy').length > 0)
  } finally {
    assert.ok(root.startsWith(resolve(tmpdir()) + sep) && root.includes('ff-frame-boundary-test-'))
    rmSync(root, { recursive: true, force: true })
  }
})
