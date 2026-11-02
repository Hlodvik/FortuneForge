import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'
import { fileURLToPath } from 'node:url'

/** Inspect the deliberately simple, literal verify-job steps; no commands run. */
export function verifyCiEmulatorOrder(workflow) {
  const lines = workflow.replaceAll('\r\n', '\n').split('\n')
  const start = lines.findIndex(line => line === '  verify:')
  if (start < 0) throw new Error('CI must have a literal verify job.')
  let end = lines.findIndex((line, index) => index > start && /^  [\w-]+:/.test(line))
  if (end < 0) end = lines.length
  const job = lines.slice(start + 1, end)
  if (job.some(line => /^    if:/.test(line))) throw new Error('The required verify job must run unconditionally.')
  if (job.some(line => /^    continue-on-error:\s*(?!false\s*$)/.test(line))) throw new Error('The required verify job must fail on errors.')
  const steps = []
  for (const line of job) {
    if (/^      - /.test(line)) steps.push([line])
    else if (steps.length) steps.at(-1).push(line)
  }
  const parsed = steps.map((block, index) => {
    const run = block.findIndex(line => /^(?:        |      - )run:/.test(line))
    const raw = run < 0 ? '' : block[run].replace(/^(?:        |      - )run:\s*/, '')
    const command = run < 0 ? '' : /^[|>][-+]?\s*$/.test(raw)
      ? block.slice(run + 1).filter(line => /^ {10}/.test(line)).map(line => line.slice(10)).join('\n')
      : raw
    return { index, name: block[0].trim(), lines: block,
      command: command.split('\n').map(line => line.trim()).filter(line => line && !line.startsWith('#')).join('\n') }
  })
  const enabled = (step, purpose) => {
    if (step.lines.some(line => /^(?:        |      - )if:/.test(line))) throw new Error(`${purpose} must run unconditionally, including on a cold cache.`)
    if (step.lines.some(line => /^(?:        |      - )continue-on-error:\s*(?!false\s*$)/.test(line))) throw new Error(`${purpose} must fail the job on error.`)
  }
  const install = parsed.find(step => /^npm install (?:--global|-g) firebase-tools@\d+\.\d+\.\d+$/m.test(step.command))
  const setup = parsed.find(step => /^firebase setup:emulators:firestore$/m.test(step.command))
  if (!install || !setup) throw new Error('CI must install a pinned Firebase CLI and provision the Firestore emulator; restoring a cache alone is insufficient.')
  enabled(install, 'Firebase CLI installation')
  enabled(setup, 'Firestore emulator provisioning')
  if (install.index > setup.index) throw new Error('Firebase CLI installation must precede emulator provisioning.')
  const node = parsed.find(step => /uses:\s*actions\/setup-node@/.test(step.lines[0]))
  if (!node || node.index >= install.index) throw new Error('Node setup must precede Firebase CLI installation.')
  const dotnetTests = parsed.filter(step => /^(?:['"])?dotnet test\b/m.test(step.command))
  if (!dotnetTests.some(step => /dotnet test games[\\/]FortuneForge\.Games\.slnx\b/.test(step.command))
    || !dotnetTests.some(step => /dotnet test FortuneForge\.Server\.Tests[\\/]FortuneForge\.Server\.Tests\.csproj\b/.test(step.command))) {
    throw new Error('CI must retain both game-package and server test suites.')
  }
  for (const step of dotnetTests) {
    enabled(step, 'Required .NET tests')
    if (step.index <= setup.index) throw new Error('Firestore emulator provisioning must precede every game-package/server test step.')
  }
  const frontendInstall = parsed.find(step => /^npm ci\s*$/m.test(step.command))
  const navbar = parsed.find(step => /^npm run verify:game-navbar\s*$/m.test(step.command))
  const frontendChecks = parsed.find(step => /^npm run (?:games:check|games:test|build|web:test)\s*$/m.test(step.command))
  if (!frontendInstall || !navbar || !frontendChecks || frontendInstall.index >= navbar.index || navbar.index >= frontendChecks.index) {
    throw new Error('The canonical navbar verifier must follow npm ci and precede frontend/game checks.')
  }
  enabled(navbar, 'Canonical navbar verification')
  return { firebaseCli: install.command.match(/firebase-tools@(\d+\.\d+\.\d+)/)[1], emulatorStep: setup.index,
    testSteps: dotnetTests.map(step => step.index), navbarStep: navbar.index }
}

if (process.argv[1] && resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  const result = verifyCiEmulatorOrder(readFileSync(resolve('.github/workflows/ci.yml'), 'utf8'))
  console.log(`Verified CI emulator provisioning before ${result.testSteps.length} .NET test steps and canonical navbar verification before frontend checks (Firebase CLI ${result.firebaseCli}).`)
}
