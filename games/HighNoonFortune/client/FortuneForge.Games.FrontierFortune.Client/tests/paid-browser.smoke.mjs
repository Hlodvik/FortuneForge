import assert from 'node:assert/strict'
import { mkdir, writeFile } from 'node:fs/promises'
import { fileURLToPath } from 'node:url'
import { chromium, request } from '@playwright/test'

// Isolated local infrastructure only; every result and award comes from the paid server.
const origin = 'http://127.0.0.1:5187'
const api = await request.newContext({ baseURL: 'http://127.0.0.1:5279' })
const health = await (await api.get('/healthz')).json()
assert.equal(health.testCreditsOnly, true)
assert.equal(health.productionEngine, true)
const directory = fileURLToPath(new URL('../../../../../artifacts/frontier-validation/', import.meta.url))
await mkdir(directory, { recursive: true })
const browser = await chromium.launch({ headless: true })
const page = await browser.newPage({ viewport: { width: 1440, height: 1000 }, reducedMotion: 'reduce' })
const errors = []
page.on('pageerror', error => errors.push(error.message))
const state = async () => {
  const reply = await api.get('/api/slots/frontier-fortune/state')
  assert.equal(reply.status(), 200)
  return reply.json()
}
const capture = name => page.screenshot({ path: `${directory}/${name}.png`, fullPage: true })
const freshPage = async () => {
  await page.goto(origin, { waitUntil: 'networkidle' })
  await page.locator('.frontier-reels, .frontier-bounty').first().waitFor()
}
const report = { testFundsOnly: true, productionFlow: true, naturalPaidSpins: 0, bonusObserved: false, checks: [] }
try {
  await freshPage()
  assert.equal(await page.getByRole('button', { name: /demo|practice|force|try bounty/i }).count(), 0)
  await page.getByRole('button', { name: 'How to play Frontier Fortune' }).click()
  await page.getByRole('dialog').waitFor({ state: 'visible' })
  assert.equal(await page.getByRole('table').locator('tbody tr').count(), 11)
  await page.getByRole('button', { name: 'Close how to play' }).click()
  report.checks.push('Paid-only controls and authoritative paytable')
  let current = await state()
  if (!current.bonus || current.bonus.completed) {
    if (current.bonus) {
      const back = page.getByRole('button', { name: 'Ride back to the reels' })
      if (await back.count()) await back.click()
    }
    await capture('frontier-desktop')
    const before = current.revision
    await page.getByRole('button', { name: /^Spin for/ }).click()
    await page.getByRole('button', { name: /^Spin for/ }).waitFor({ state: 'visible' }).catch(() => {})
    await page.waitForFunction(() => document.querySelector('[aria-busy="true"]') === null)
    current = await state()
    assert.equal(current.revision, before + 1)
    report.checks.push('UI spin traverses the production paid settlement')
    await page.setViewportSize({ width: 390, height: 844 })
    await capture('frontier-mobile')
    assert.equal(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth), true)
  }
  // Natural cryptographic outcomes and real test-fund settlements; no trigger injection.
  for (let attempt = 0; attempt < 1600 && (!current.bonus || current.bonus.completed); attempt++) {
    const reply = await api.post('/api/slots/frontier-fortune/spins', {
      data: { operationId: crypto.randomUUID(), wagerPoints: current.wagering.minWagerPoints },
    })
    assert.equal(reply.status(), 200, await reply.text())
    current = await reply.json()
    report.naturalPaidSpins++
    if (attempt % 50 === 0) process.stdout.write(`Natural paid spins: ${attempt + 1}\n`)
  }
  if (current.bonus && !current.bonus.completed) {
    report.bonusObserved = true
    await page.setViewportSize({ width: 1440, height: 1000 })
    await freshPage()
    await capture('frontier-bounty-desktop')
    const original = structuredClone(current.bonus)
    await page.reload({ waitUntil: 'networkidle' })
    assert.deepEqual((await state()).bonus, original)
    report.checks.push('Naturally earned bounty survives reload with identical state')
    await page.setViewportSize({ width: 390, height: 844 })
    await capture('frontier-bounty-mobile')
    assert.equal(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth), true)
    const target = original.cells.find(cell => cell.kind === 'ricochet' && cell.hitPoints > 0)
    assert.ok(target)
    const preview = original.previews.find(item => item.targetIndex === target.index)
    await page.locator('.frontier-target').nth(target.index).click()
    assert.equal(await page.locator('[data-affected="true"]').count(), preview.hitIndices.length)
    await page.getByRole('button', { name: 'Take the shot' }).click()
    await page.waitForFunction(() => !document.querySelector('.frontier-bounty__actions button:disabled'))
    current = await state()
    assert.equal(current.bonus.collectedPoints, original.collectedPoints + preview.awardPoints)
    assert.equal(current.bonus.shotsRemaining, preview.shotsAfter)
    report.checks.push('Player selection matches the exact server ricochet preview')
    if (!current.bonus.completed) {
      const payout = current.bonus.collectedPoints * current.wagering.pointValueInCents
      const balance = current.balanceCents
      await page.getByRole('button', { name: /^Bank R/ }).click()
      await page.getByRole('button', { name: 'Ride back to the reels' }).waitFor()
      current = await state()
      assert.equal(current.balanceCents, balance + payout)
      assert.equal(current.bonus.completed, true)
      report.checks.push('Banking settles the collected award once through the wallet')
    }
  }
  assert.deepEqual(errors, [])
  report.checks.push('No browser runtime errors; desktop and mobile have no horizontal overflow')
  report.finalState = { revision: current.revision, balanceCents: current.balanceCents, bonusCompleted: current.bonus?.completed }
  await writeFile(`${directory}/paid-browser-report.json`, JSON.stringify(report, null, 2) + '\n')
  process.stdout.write(JSON.stringify(report, null, 2) + '\n')
} finally {
  await browser.close()
  await api.dispose()
}
