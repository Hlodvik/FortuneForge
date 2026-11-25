import { chromium, expect } from '@playwright/test'
import { mkdirSync, writeFileSync } from 'node:fs'
import { fileURLToPath } from 'node:url'

const output = fileURLToPath(new URL('../../artifacts/client-verification/browser/', import.meta.url))
mkdirSync(output, { recursive: true })
const browser = await chromium.launch({ headless: true })
const reports = []
try {
  const games = process.argv.slice(2).length ? process.argv.slice(2) : ['pirates', 'olympus', 'jade']
  if (games.some(game => !['pirates', 'olympus', 'jade', 'sunstone'].includes(game))) throw Error('Unknown isolated test game.')
  for (const game of games) {
    const page = await browser.newPage({ viewport: { width: 1440, height: 1000 }, reducedMotion: 'reduce' })
    const errors = []
    page.on('pageerror', issue => errors.push(issue.message))
    const url = `http://127.0.0.1:5180/tools/FortuneForge.SlotClient.LocalHarness/review.html?game=${game}`
    const walletUrl = `http://127.0.0.1:5180/fixture/${game}/__fixture/wallet`
    const before = await (await page.request.get(walletUrl)).json()
    try {
      await page.goto(url, { waitUntil: 'domcontentloaded' })
      const surface = game === 'jade' ? page.frameLocator('iframe') : page
      const spin = game === 'jade' ? surface.locator('#spin') : surface.getByRole('button', { name: 'Spin the reels', exact: true })
      await expect(spin).toBeEnabled({ timeout: 20000 })
      const responsePromise = page.waitForResponse(response => response.url().includes('/api/slots/prepared-spins') && response.request().method() === 'POST')
      await spin.click()
      const response = await responsePromise
      if (!response.ok()) throw Error(`Paid spin failed: ${response.status()} ${await response.text()}`)
      const first = await response.json()
      if (game === 'jade') await expect(spin).toHaveAttribute('aria-busy', 'false')
      else await page.waitForFunction(() => !document.querySelector('[data-game-busy]'))
      await page.screenshot({ path: `${output}/${game}-desktop.png`, fullPage: true })

      // Lose a real settled HTTP reply, then exercise this client's production recovery.
      const attempts = []
      let lost = false
      await page.route(`**/fixture/${game}/api/slots/prepared-spins`, async route => {
        attempts.push({ body: route.request().postData(), operation: route.request().headers()['idempotency-key'] })
        if (!lost) {
          lost = true
          const settled = await route.fetch()
          if (!settled.ok()) throw Error(`Interrupted spin did not settle: ${settled.status()} ${await settled.text()}`)
          await route.abort('failed')
        } else await route.continue()
      })
      await page.waitForTimeout(600)
      await spin.click()
      if (game === 'jade') {
        await expect(surface.locator('#message')).toContainText('Reload to recover', { timeout: 15000 })
        await page.reload({ waitUntil: 'domcontentloaded' })
        await expect(page.frameLocator('iframe').locator('#spin')).toBeEnabled({ timeout: 20000 })
      } else {
        await expect(surface.getByRole('alert')).toBeVisible()
        await surface.getByRole('button', { name: 'Reconnect / recover' }).click()
        await expect(surface.getByRole('alert')).toHaveCount(0)
        await expect(spin).toBeEnabled()
      }
      expect(attempts).toHaveLength(2)
      expect(attempts[1]).toEqual(attempts[0])
      const after = await (await page.request.get(walletUrl)).json()
      expect(after.testFundsOnly).toBe(true)
      expect(after.settlements).toHaveLength(before.settlements.length + 2)
      expect(after.balanceCents).toBe(1_000_000 + after.settlements.reduce((sum, entry) => sum - entry.request.wagerCents + entry.request.payoutCents, 0))
      expect(errors).toEqual([])
      await page.setViewportSize({ width: 390, height: 844 })
      await page.screenshot({ path: `${output}/${game}-mobile.png`, fullPage: true })
      const report = { game, testFundsOnly: true, productionEngine: true, firstSpinId: first.spinId,
        recoveryOperationId: attempts[0].operation, exactlyOnceRecovery: true, newSettlements: 2, walletBalanceCents: after.balanceCents, browserErrors: errors }
      reports.push(report)
      console.log(JSON.stringify(report))
    } catch (issue) {
      await page.screenshot({ path: `${output}/${game}-failure.png`, fullPage: true }).catch(() => {})
      console.error(JSON.stringify({ game, errors, text: (await page.locator('body').innerText()).slice(0, 1800) }))
      throw issue
    } finally { await page.close() }
  }
  writeFileSync(`${output}/paid-clients.json`, JSON.stringify(reports, null, 2) + '\n')
} finally { await browser.close() }
