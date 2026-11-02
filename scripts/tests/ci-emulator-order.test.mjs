import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import test from 'node:test'
import { verifyCiEmulatorOrder } from '../verify-ci-emulator-order.mjs'

const setup = `      - name: Provision emulator
        run: |
          npm install --global firebase-tools@14.4.0
          firebase setup:emulators:firestore`
const games = `      - name: Game-package tests
        run: dotnet test games/FortuneForge.Games.slnx --configuration Release --no-build`
const server = `      - name: Server tests
        run: |
          firebase emulators:exec --only firestore \\
            'dotnet test FortuneForge.Server.Tests/FortuneForge.Server.Tests.csproj --configuration Release --no-build'`
const frontend = `      - name: Dependencies
        run: npm ci
      - name: Shared navbar
        run: npm run verify:game-navbar
      - name: Frontend checks
        run: npm run games:check`
const workflow = blocks => `jobs:\n  verify:\n    runs-on: ubuntu-latest\n    steps:\n      - uses: actions/setup-node@v7\n${blocks.join('\n')}\n  slots-visual:\n    steps:\n      - run: echo other job\n`

test('current workflow provisions a cold-cache emulator before both required test suites', () => {
  const result = verifyCiEmulatorOrder(readFileSync(new URL('../../.github/workflows/ci.yml', import.meta.url), 'utf8'))
  assert.equal(result.testSteps.length, 2)
  assert.equal(result.firebaseCli, '14.4.0')
})
test('accepts unconditional provisioning independently of a cache restore', () => {
  assert.equal(verifyCiEmulatorOrder(workflow([setup, games, server, frontend])).testSteps.length, 2)
})
test('rejects the diagnosed test-before-emulator ordering', () => {
  assert.throws(() => verifyCiEmulatorOrder(workflow([games, setup, server, frontend])), /provisioning must precede/)
})
test('a cache restore cannot substitute for cold-cache provisioning', () => {
  assert.throws(() => verifyCiEmulatorOrder(workflow(['      - uses: actions/cache@v4', games, server, frontend])), /cache alone is insufficient/)
})
test('cache-hit conditions cannot skip provisioning', () => {
  assert.throws(() => verifyCiEmulatorOrder(workflow([setup.replace('        run:', "        if: steps.cache.outputs.cache-hit != 'true'\n        run:"), games, server, frontend])), /unconditionally/)
  assert.throws(() => verifyCiEmulatorOrder(workflow([setup.replace('      - name: Provision emulator', '      - if: false\n        name: Provision emulator'), games, server, frontend])), /unconditionally/)
})
test('the required verify job cannot be conditional or ignore failures', () => {
  assert.throws(() => verifyCiEmulatorOrder(workflow([setup, games, server, frontend]).replace('    runs-on:', '    if: false\n    runs-on:')), /unconditionally/)
  assert.throws(() => verifyCiEmulatorOrder(workflow([setup, games, server, frontend]).replace('    runs-on:', '    continue-on-error: true\n    runs-on:')), /fail on errors/)
})
test('provisioning errors cannot be ignored', () => {
  assert.throws(() => verifyCiEmulatorOrder(workflow([setup.replace('        run:', '        continue-on-error: true\n        run:'), games, server, frontend])), /fail the job/)
  for (const suffix of [' || true', '; true']) assert.throws(() => verifyCiEmulatorOrder(workflow([setup.replace('firebase setup:emulators:firestore', `firebase setup:emulators:firestore${suffix}`), games, server, frontend])), /provision/)
})
test('an echoed or commented setup command does not provision an emulator', () => {
  for (const prefix of ['echo ', '# ']) assert.throws(() => verifyCiEmulatorOrder(workflow([setup.replace('          firebase setup:', `          ${prefix}firebase setup:`), games, server, frontend])), /provision/)
})
test('unversioned tooling does not satisfy reproducible provisioning', () => {
  assert.throws(() => verifyCiEmulatorOrder(workflow([setup.replace('@14.4.0', '@latest'), games, server, frontend])), /pinned/)
})
test('retains both required suites and their fatal failures', () => {
  assert.throws(() => verifyCiEmulatorOrder(workflow([setup, games, frontend])), /both game-package and server/)
  assert.throws(() => verifyCiEmulatorOrder(workflow([setup, games.replace('        run:', '        continue-on-error: true\n        run:'), server, frontend])), /fail the job/)
})
test('an emulator in another job cannot satisfy the verify job', () => {
  const source = workflow([games, server, frontend]).replace('      - run: echo other job', setup)
  assert.throws(() => verifyCiEmulatorOrder(source), /provision/)
})
test('keeps the navbar guard after dependency install and before frontend checks', () => {
  assert.throws(() => verifyCiEmulatorOrder(workflow([setup, games, server, frontend.replace('        run: npm run verify:game-navbar', '        run: echo skipped')])), /navbar verifier/)
  assert.throws(() => verifyCiEmulatorOrder(workflow([setup, games, server, frontend.replace('        run: npm ci', '        run: npm run games:check')])), /navbar verifier/)
})
