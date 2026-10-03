import { expect, test, type Page } from '@playwright/test'
import { mkdir } from 'node:fs/promises'
import { resolve } from 'node:path'
import type { AccountSummary } from '../../src/features/account/services/accountsApi'
import type { FlappyFreeRun, FlappyReplayCompletion } from '../../src/games/arcade/arcadeCompetitionApi'

// Seeded public free-run/replay DTO fixtures test the browser surface, not RNG, settlement or payment.
type Write = { path: string; key?: string; body: unknown }
type Reply = { abort?: boolean; status?: number; payload?: unknown; gate?: Promise<void> }
const userId = 'flappy-ux-viewer'
const api = '/api/arcade-competitions/flappy/free/runs'
const evidence = resolve(process.cwd(), '..', '.artifacts/ux-cycle/flappy/browser')
const viewports = [
  { name: 'desktop', width: 1280, height: 720 },
  { name: 'phone', width: 390, height: 844 },
  { name: 'short-phone', width: 390, height: 700 },
  { name: 'compact-phone', width: 320, height: 568 },
  { name: 'narrow-landscape', width: 500, height: 320 },
  { name: 'short-landscape', width: 568, height: 320 },
  { name: 'landscape', width: 667, height: 375 },
  { name: 'wide-landscape', width: 852, height: 393 },
]
function runFixture(index = 1): FlappyFreeRun {
  return { runId: 'flappy_free_' + index.toString(16).padStart(64, '0'), seed: 424242, startedAtUtc: '2026-10-01T00:00:00Z', wasReplay: false }
}
function deferred() {
  let release!: () => void
  const promise = new Promise<void>(resolve => { release = resolve })
  return { promise, release }
}
async function installApi(page: Page) {
  const h = {
    user: userId,
    writes: [] as Write[],
    starts: new Map<string, FlappyFreeRun>(),
    resultScore: 0,
    reply: null as ((write: Write) => Reply | Promise<Reply>) | null,
  }
  await page.addInitScript(() => sessionStorage.setItem('fortune-forge.account-token', 'flappy-ux-session'))
  await page.route('**/api/accounts/me', route => route.fulfill({ json: {
    userId: h.user, playerName: 'Ada River', email: 'flappy-ux@example.invalid', createdAtUtc: '2026-10-01T00:00:00Z', role: 'player',
    balances: { slotsCredits: 100, freeGames: 0 }, slots: { spinsPlayed: 0, wins: 0, losses: 0, creditsWagered: 0, creditsWon: 0, netCredits: 0 },
  } satisfies AccountSummary }))
  await page.route('**/api/arcade-competitions/flappy/**', async route => {
    const request = route.request(), path = new URL(request.url()).pathname
    if (request.method() !== 'POST') return route.fulfill({ status: 404, json: { code: 'unexpected-read' } })
    const write: Write = { path, key: request.headers()['idempotency-key'], body: request.postData() ? request.postDataJSON() : null }
    h.writes.push(write)
    let fallback: FlappyFreeRun | FlappyReplayCompletion
    if (path === api) {
      const key = write.key ?? ''
      let run = h.starts.get(key)
      if (!run) { run = runFixture(h.starts.size + 1); h.starts.set(key, run) }
      fallback = run
    } else {
      fallback = { runId: path.slice(api.length + 1, -'/replay'.length), score: h.resultScore, terminal: 'ground-collision', wasReplay: false }
    }
    const reply = h.reply ? await h.reply(write) : {}
    if (reply.gate) await reply.gate
    if (reply.abort) return route.abort('failed')
    return route.fulfill({ status: reply.status ?? 200, json: reply.payload ?? fallback })
  })
  return h
}
async function open(page: Page) {
  await page.goto('/games/flappy')
  await expect(page.locator('.flappy-free-run-page')).toBeVisible()
}
async function capture(page: Page, name: string) {
  await mkdir(evidence, { recursive: true })
  await page.screenshot({ path: resolve(evidence, name + '.png'), animations: 'disabled' })
}
async function fits(page: Page, selectors = '.flappy-course,.flappy-controls,.flappy-free-run-page button') {
  const problems = await page.evaluate(selectors => {
    const failures: string[] = []
    if (document.documentElement.scrollHeight > innerHeight + 1 || document.documentElement.scrollWidth > innerWidth + 1) failures.push('document overflow')
    for (const element of document.querySelectorAll<HTMLElement>(selectors)) {
      const r = element.getBoundingClientRect()
      if (!r.width || !r.height) continue
      const name = element.getAttribute('aria-label') ?? element.className ?? element.textContent
      if (r.left < -1 || r.top < -1 || r.right > innerWidth + 1 || r.bottom > innerHeight + 1) failures.push(`outside viewport: ${name}`)
      if (element.tagName === 'BUTTON' && !element.matches(':disabled')) {
        const hit = document.elementFromPoint(r.x + r.width / 2, r.y + r.height / 2)
        if (!hit || !element.contains(hit)) failures.push(`covered: ${name}`)
      }
      for (let ancestor = element.parentElement; ancestor; ancestor = ancestor.parentElement) {
        const style = getComputedStyle(ancestor), a = ancestor.getBoundingClientRect()
        if (/(hidden|clip|auto|scroll)/.test(style.overflowX) && (r.left < a.left - 1 || r.right > a.right + 1)) failures.push(`clipped horizontally: ${name}`)
        if (/(hidden|clip|auto|scroll)/.test(style.overflowY) && (r.top < a.top - 1 || r.bottom > a.bottom + 1)) failures.push(`clipped vertically: ${name}`)
      }
    }
    return [...new Set(failures)]
  }, selectors)
  expect(problems).toEqual([])
  const course = await page.locator('.flappy-course').boundingBox()
  expect(course).not.toBeNull()
  const viewport = page.viewportSize()!
  const portraitMobile = viewport.width <= 720 && !(viewport.height <= 500 && viewport.width >= 500)
  expect(Math.abs(course!.width / course!.height - (portraitMobile ? 3 / 4 : 4 / 3))).toBeLessThan(.01)
}
async function pose(page: Page) {
  return page.locator('.flappy-course svg').evaluate(element => element.outerHTML)
}
const recoveryKey = `fortuneforge:flappy:recovery:${userId}`
async function stored(page: Page, key = recoveryKey) {
  return page.evaluate(key => JSON.parse(sessionStorage.getItem(key) ?? 'null'), key)
}
async function freezeClock(page: Page) {
  // Lazy route/account bootstrap finishes before pausing the browser clock.
  await page.clock.install()
  await page.clock.pauseAt(await page.evaluate(() => Date.now() + 100))
}
async function reloadPausedClock(page: Page) {
  // Navigation re-runs lazy suspense/bootstrap timers, which need a running clock until the page is visible.
  await page.clock.resume(); await page.reload()
  await expect(phase(page)).toBeVisible()
  await page.clock.pauseAt(await page.evaluate(() => Date.now() + 100))
}
async function begin(page: Page) {
  await page.getByRole('button', { name: 'Start flight', exact: true }).click()
  await expect(page.getByRole('button', { name: 'Pause', exact: true })).toBeVisible()
  await page.clock.runFor(120)
}
async function terminal(page: Page) {
  await page.clock.runFor(2500)
  await expect.poll(async () => (await stored(page))?.kind).not.toBe('flight')
}
const phase = (page: Page) => page.locator('.flappy-free-run-page')
const button = (page: Page, name: string) => page.getByRole('button', { name, exact: true })
async function rules(page: Page, image: string) {
  const opener = button(page, 'Rules')
  await opener.click()
  const dialog = page.getByRole('dialog', { name: 'Flappy rules', exact: true })
  await expect(dialog).toBeVisible()
  await expect(button(page, 'Close flight details')).toBeFocused()
  expect(await dialog.evaluate(e => { const r = e.getBoundingClientRect(); return r.left >= 0 && r.top >= 0 && r.right <= innerWidth && r.bottom <= innerHeight })).toBe(true)
  for (let index = 0; index < 6; index++) {
    await page.keyboard.press(index % 2 ? 'Shift+Tab' : 'Tab')
    expect(await dialog.evaluate(e => e.contains(document.activeElement))).toBe(true)
  }
  const body = dialog.locator('.flappy-dialog-body')
  await body.focus(); await page.keyboard.press('End'); await page.clock.runFor(100)
  if (await body.evaluate(e => e.scrollHeight > e.clientHeight + 1)) await expect.poll(() => body.evaluate(e => e.scrollTop)).toBeGreaterThan(0)
  await expect(dialog).toContainText('Tap the course')
  await capture(page, image)
  await page.keyboard.press('Escape')
  await expect(dialog).toHaveCount(0)
  await expect(opener).toBeFocused()
}

test.describe('Recorded Flappy browser surface', () => {
  for (const viewport of viewports) test(`${viewport.name}: flight, pause, rules and confirmed result keep the course and controls in view`, async ({ page }) => {
    await page.setViewportSize(viewport)
    const h = await installApi(page)
    await open(page); await freezeClock(page)
    await expect(phase(page)).toHaveAttribute('data-phase', 'lobby')
    await expect(page.locator('.game-ambient-toggle')).toHaveCount(0)
    await fits(page); const courseBox = await page.locator('.flappy-course').boundingBox()
    await capture(page, viewport.name + '-lobby')
    const startGate = deferred()
    h.reply = () => ({ gate: startGate.promise })
    await button(page, 'Start flight').click()
    await expect(phase(page)).toHaveAttribute('data-phase', 'starting')
    await expect(button(page, 'Preparing flight…')).toBeDisabled()
    expect(h.writes).toHaveLength(1)
    expect(h.writes[0]).toMatchObject({ path: api, body: null })
    expect(h.writes[0].key).toMatch(/^[A-Za-z0-9_-]{16,128}$/)
    await fits(page); await capture(page, viewport.name + '-starting')
    h.reply = null; startGate.release()
    await expect(phase(page)).toHaveAttribute('data-phase', 'playing')
    await page.clock.runFor(120)
    await fits(page); await capture(page, viewport.name + '-playing')
    await button(page, 'Pause').click()
    await expect(phase(page)).toHaveAttribute('data-phase', 'paused')
    const pausedPose = await pose(page), cursor = await stored(page)
    await page.clock.runFor(2000)
    expect(await pose(page)).toBe(pausedPose); expect(await stored(page)).toEqual(cursor)
    await fits(page); await capture(page, viewport.name + '-paused')
    await rules(page, viewport.name + '-rules')
    await expect(phase(page)).toHaveAttribute('data-phase', 'paused')
    expect(h.writes).toHaveLength(1)
    const completionGate = deferred()
    h.reply = () => ({ gate: completionGate.promise })
    await button(page, 'Resume flight').click()
    await terminal(page)
    await expect(phase(page)).toHaveAttribute('data-phase', 'submitting')
    expect(h.writes).toHaveLength(2)
    const replay = h.writes[1].body as { totalTicks: number; flapTicks: number[] }
    expect(Object.keys(replay).sort()).toEqual(['flapTicks', 'totalTicks'])
    expect(replay.totalTicks).toBeGreaterThan(0); expect(replay.flapTicks).toEqual([0])
    await expect(page.locator('.flappy-scene-bird')).toHaveClass(/is-collided/)
    await expect(page.locator('.flappy-result-score')).toHaveText('0')
    await fits(page); await capture(page, viewport.name + '-recording')
    h.reply = null; completionGate.release()
    await expect(phase(page)).toHaveAttribute('data-phase', 'result')
    await expect(page.locator('.flappy-result-score')).toHaveAttribute('aria-label', 'Official score 0')
    await expect(page.locator('.flappy-result-board')).toContainText('Best0')
    await fits(page); await capture(page, viewport.name + '-result')
    const finalBox = await page.locator('.flappy-course').boundingBox()
    for (const field of ['x', 'y', 'width', 'height'] as const) expect(Math.abs(courseBox![field] - finalBox![field]), `persistent course ${field}`).toBeLessThanOrEqual(1)
    await button(page, 'Fly again').click()
    await expect(phase(page)).toHaveAttribute('data-phase', 'playing')
    expect(h.writes).toHaveLength(3); expect(h.writes[2].key).not.toBe(h.writes[0].key)
    await fits(page); await capture(page, viewport.name + '-again')
  })

  test('an uncertain start survives reload with no automatic write and retries the exact key', async ({ page }) => {
    const h = await installApi(page); h.reply = () => ({ abort: true })
    await open(page); await freezeClock(page)
    await button(page, 'Start flight').click()
    await expect(phase(page)).toHaveAttribute('data-phase', 'start-failed')
    const saved = await stored(page)
    expect(saved).toEqual({ kind: 'start', idempotencyKey: h.writes[0].key, mode: 'free' })
    await capture(page, 'start-interrupted')
    await reloadPausedClock(page)
    await expect(phase(page)).toHaveAttribute('data-phase', 'start-failed')
    await page.clock.runFor(2000)
    expect(h.writes).toHaveLength(1)
    h.reply = null
    await button(page, 'Retry flight start').click()
    await expect(phase(page)).toHaveAttribute('data-phase', 'playing')
    expect(h.writes).toHaveLength(2); expect(h.writes[1]).toEqual(h.writes[0])
    expect((await stored(page)).run.runId).toBe(runFixture().runId)
  })

  test('an uncertain completed replay survives reload and repeated rejection without changing its input', async ({ page }) => {
    const h = await installApi(page)
    await open(page); await freezeClock(page); await begin(page)
    h.reply = () => ({ abort: true }); await terminal(page)
    await expect(phase(page)).toHaveAttribute('data-phase', 'submit-failed')
    const original = h.writes[1], saved = await stored(page)
    expect(saved.kind).toBe('submission')
    await capture(page, 'recording-interrupted')
    await reloadPausedClock(page); await expect(phase(page)).toHaveAttribute('data-phase', 'submit-failed')
    await page.clock.runFor(1500); expect(h.writes).toHaveLength(2)
    h.reply = () => ({ status: 400, payload: { code: 'arcade-flappy-replay-invalid' } })
    await button(page, 'Retry recording').click()
    await expect(phase(page)).toHaveAttribute('data-phase', 'submit-failed')
    expect(h.writes[2]).toEqual(original); expect(await stored(page)).toEqual(saved)
    await expect(page.getByRole('alert')).toContainText('saved replay is still available')
    h.reply = null; await button(page, 'Retry recording').click()
    await expect(phase(page)).toHaveAttribute('data-phase', 'result')
    expect(h.writes[3]).toEqual(original); expect(await stored(page)).toBeNull()
  })

  for (const viewport of [viewports[0], viewports[1], viewports[4], viewports[7]]) test(`${viewport.name}: valid partial flight restores paused with visible openings and exact earlier input`, async ({ page }) => {
    await page.setViewportSize(viewport)
    const h = await installApi(page); h.resultScore = 1
    const cursor = { totalTicks: 230, flapTicks: [7, 39, 72, 104, 126, 143, 173, 206] }
    // Derived once from the unchanged public replay mirror. This is presentation setup, not an engine assertion.
    await page.addInitScript(({ key, run, cursor }) => sessionStorage.setItem(key, JSON.stringify({ kind: 'flight', run, cursor })), { key: recoveryKey, run: runFixture(), cursor })
    await open(page); await freezeClock(page)
    await expect(phase(page)).toHaveAttribute('data-phase', 'paused')
    await expect(page.locator('.flappy-score')).toContainText('1')
    await expect(page.locator('.flappy-scene-bird')).toHaveAttribute('data-tick', '230')
    await page.clock.runFor(2000); expect(h.writes).toEqual([])
    await fits(page); await capture(page, viewport.name + '-restored-course')
    await button(page, 'Resume flight').click(); await page.clock.runFor(40)
    await expect(phase(page)).toHaveAttribute('data-phase', 'playing')
    await fits(page); await capture(page, viewport.name + '-near-pipes')
    await terminal(page); await expect(phase(page)).toHaveAttribute('data-phase', 'result')
    expect(h.writes).toHaveLength(1)
    expect(h.writes[0].path).toBe(api + '/' + runFixture().runId + '/replay')
    const replay = h.writes[0].body as { totalTicks: number; flapTicks: number[] }
    expect(replay.flapTicks).toEqual(cursor.flapTicks); expect(replay.totalTicks).toBeGreaterThan(cursor.totalTicks)
    await expect(page.locator('.flappy-result-score')).toHaveAttribute('aria-label', 'Official score 1')
    await fits(page); await capture(page, viewport.name + '-near-pipes-result')
  })

  test('Space repeat and nonprimary pointers do not add flaps; one course press adds one input and Escape pauses', async ({ page }) => {
    await installApi(page); await open(page); await freezeClock(page); await begin(page)
    const course = page.locator('.flappy-course')
    await course.focus()
    const original = (await stored(page)).cursor.flapTicks
    await page.keyboard.down('Space'); await page.keyboard.down('Space')
    await page.clock.runFor(20)
    const pressed = (await stored(page)).cursor.flapTicks
    expect(pressed).toHaveLength(original.length + 1)
    await page.keyboard.down('Space'); await page.clock.runFor(20); await page.keyboard.up('Space')
    expect((await stored(page)).cursor.flapTicks).toEqual(pressed)
    await page.keyboard.press('KeyA'); await page.clock.runFor(20)
    await course.dispatchEvent('pointerdown', { button: 2, isPrimary: true }); await page.clock.runFor(20)
    await course.dispatchEvent('pointerdown', { button: 0, isPrimary: false }); await page.clock.runFor(20)
    expect((await stored(page)).cursor.flapTicks).toEqual(pressed)
    const r = await course.boundingBox()
    await page.mouse.move(r!.x + r!.width * .7, r!.y + r!.height * .6)
    await page.mouse.down(); await page.clock.runFor(20); await page.mouse.up()
    const afterClick = (await stored(page)).cursor.flapTicks
    expect(afterClick).toHaveLength(pressed.length + 1)
    await page.clock.runFor(20); expect((await stored(page)).cursor.flapTicks).toEqual(afterClick)
    await page.keyboard.press('Escape'); await expect(phase(page)).toHaveAttribute('data-phase', 'paused')
    const beforeResume = (await stored(page)).cursor.totalTicks
    await page.keyboard.press('Space'); await page.clock.runFor(20)
    await expect(phase(page)).toHaveAttribute('data-phase', 'playing')
    expect((await stored(page)).cursor.flapTicks.at(-1)).toBe(beforeResume)
    await capture(page, 'keyboard-course-input')
  })

  test('real browser touch on Flap and course each records a single primary input without page movement', async ({ browser }) => {
    const context = await browser.newContext({ viewport: { width: 390, height: 844 }, hasTouch: true, isMobile: true, deviceScaleFactor: 1, baseURL: 'http://127.0.0.1:4191', reducedMotion: 'reduce' })
    const page = await context.newPage()
    try {
      await installApi(page); await open(page); await freezeClock(page); await begin(page)
      const before = (await stored(page)).cursor.flapTicks
      await button(page, 'Flap').tap(); await page.clock.runFor(20)
      const afterFlap = (await stored(page)).cursor.flapTicks
      expect(afterFlap).toHaveLength(before.length + 1)
      const r = await page.locator('.flappy-course').boundingBox()
      await page.touchscreen.tap(r!.x + r!.width * .75, r!.y + r!.height * .65); await page.clock.runFor(20)
      expect((await stored(page)).cursor.flapTicks).toHaveLength(afterFlap.length + 1)
      expect(await page.evaluate(() => ({ x: scrollX, y: scrollY }))).toEqual({ x: 0, y: 0 })
      await fits(page); await capture(page, 'touch-single-flap')
    } finally { await context.close() }
  })

  test('blur and simulated document hiding freeze the saved flight until explicit resume', async ({ page }) => {
    const h = await installApi(page); await open(page); await freezeClock(page); await begin(page)
    await page.evaluate(() => window.dispatchEvent(new Event('blur')))
    await expect(phase(page)).toHaveAttribute('data-phase', 'paused')
    const paused = await stored(page), pausedPose = await pose(page)
    await page.clock.runFor(10_000)
    expect(await stored(page)).toEqual(paused); expect(await pose(page)).toBe(pausedPose)
    expect(h.writes).toHaveLength(1)
    await button(page, 'Resume flight').click(); await page.clock.runFor(20)
    await page.evaluate(() => {
      Object.defineProperty(document, 'hidden', { configurable: true, value: true })
      document.dispatchEvent(new Event('visibilitychange'))
    })
    await expect(phase(page)).toHaveAttribute('data-phase', 'paused')
    const hidden = await stored(page)
    await page.clock.runFor(10_000); expect(await stored(page)).toEqual(hidden)
    await page.evaluate(() => {
      Object.defineProperty(document, 'hidden', { configurable: true, value: false })
      document.dispatchEvent(new Event('visibilitychange'))
    })
    await expect(phase(page)).toHaveAttribute('data-phase', 'paused')
    await capture(page, 'visibility-paused')
  })

  test('storage failure blocks a new start before any write', async ({ page }) => {
    const h = await installApi(page)
    await page.addInitScript(() => {
      const nativeSet = Storage.prototype.setItem
      Storage.prototype.setItem = function (key, value) {
        if (key.startsWith('fortuneforge:flappy:')) throw new DOMException('Test storage blocked', 'QuotaExceededError')
        return nativeSet.call(this, key, value)
      }
    })
    await open(page); await button(page, 'Start flight').click()
    await expect(page.getByRole('alert')).toContainText('browser could not save your flight')
    expect(h.writes).toEqual([])
    await expect(phase(page)).toHaveAttribute('data-phase', 'lobby')
    await capture(page, 'storage-unavailable')
  })

  test('corrupt saved input needs explicit clearing and never posts itself', async ({ page }) => {
    const h = await installApi(page)
    await page.addInitScript(key => sessionStorage.setItem(key, '{not valid json'), recoveryKey)
    await open(page)
    await expect(phase(page)).toHaveAttribute('data-phase', 'unavailable')
    expect(h.writes).toEqual([])
    await button(page, 'Clear saved flight').click()
    const dialog = page.getByRole('dialog', { name: 'Clear this flight?', exact: true })
    await expect(dialog).toBeVisible()
    await button(page, 'Clear flight').click()
    await expect(dialog).toHaveCount(0); await expect(phase(page)).toHaveAttribute('data-phase', 'lobby')
    expect(await stored(page)).toBeNull(); expect(h.writes).toEqual([])
    await capture(page, 'corrupt-input-cleared')
  })

  test('recovery belongs to its account and does not leak across an account reload', async ({ page }) => {
    const h = await installApi(page); h.reply = () => ({ abort: true })
    await open(page); await button(page, 'Start flight').click()
    await expect(phase(page)).toHaveAttribute('data-phase', 'start-failed')
    const original = await stored(page)
    h.user = 'flappy-other-viewer'; await page.reload()
    await expect(phase(page)).toHaveAttribute('data-phase', 'lobby')
    expect(h.writes).toHaveLength(1); expect(await stored(page)).toEqual(original)
    expect(await stored(page, 'fortuneforge:flappy:recovery:flappy-other-viewer')).toBeNull()
    h.user = userId; await page.reload()
    await expect(phase(page)).toHaveAttribute('data-phase', 'start-failed')
    expect(h.writes).toHaveLength(1); expect(await stored(page)).toEqual(original)
  })

  test('a malformed receipt cannot replace the pending replay or show an official result', async ({ page }) => {
    const h = await installApi(page)
    await open(page); await freezeClock(page); await begin(page)
    h.reply = () => ({ payload: { runId: runFixture(999).runId, score: 99, terminal: 'ground-collision', wasReplay: false } })
    await terminal(page); await expect(phase(page)).toHaveAttribute('data-phase', 'submit-failed')
    const saved = await stored(page), original = h.writes[1]
    await expect(page.locator('.flappy-result-score')).not.toHaveAttribute('aria-label', /Official score/)
    expect(saved.kind).toBe('submission')
    h.reply = null; h.resultScore = 4
    await button(page, 'Retry recording').click(); await expect(phase(page)).toHaveAttribute('data-phase', 'result')
    expect(h.writes[2]).toEqual(original)
    await expect(page.locator('.flappy-result-score')).toHaveAttribute('aria-label', 'Official score 4')
    await expect(page.locator('.flappy-result-board')).toContainText('Best4')
    await capture(page, 'authoritative-score-after-retry')
  })

  test('clearing an unfinished flight requires its bounded keyboard dialog and Escape preserves the input', async ({ page }) => {
    await page.setViewportSize({ width: 568, height: 320 })
    const h = await installApi(page)
    await open(page); await freezeClock(page); await begin(page); await button(page, 'Pause').click()
    const saved = await stored(page)
    await button(page, 'New flight').click()
    const dialog = page.getByRole('dialog', { name: 'Clear this flight?', exact: true })
    await expect(dialog).toBeVisible()
    expect(await dialog.evaluate(e => { const r = e.getBoundingClientRect(); return r.left >= 0 && r.top >= 0 && r.right <= innerWidth && r.bottom <= innerHeight })).toBe(true)
    for (let index = 0; index < 5; index++) {
      await page.keyboard.press('Tab'); expect(await dialog.evaluate(e => e.contains(document.activeElement))).toBe(true)
    }
    await capture(page, 'clear-flight-confirmation')
    await page.keyboard.press('Escape'); await expect(dialog).toHaveCount(0)
    expect(await stored(page)).toEqual(saved); expect(h.writes).toHaveLength(1)
    await button(page, 'New flight').click(); await button(page, 'Clear flight').click()
    await expect(phase(page)).toHaveAttribute('data-phase', 'lobby')
    expect(await stored(page)).toBeNull(); expect(h.writes).toHaveLength(1)
  })

  test('reduced motion removes cosmetic tilt while the same replay frame and input remain intact', async ({ browser }) => {
    const frames: { y: string | null; transform: string | null; cursor: unknown }[] = []
    for (const reducedMotion of ['reduce', 'no-preference'] as const) {
      const context = await browser.newContext({ baseURL: 'http://127.0.0.1:4191', reducedMotion })
      const page = await context.newPage()
      try {
        await installApi(page); await open(page); await freezeClock(page); await begin(page)
        frames.push({ y: await page.locator('.flappy-scene-bird').getAttribute('data-bird-y'), transform: await page.locator('.flappy-scene-bird').getAttribute('transform'), cursor: (await stored(page)).cursor })
        await capture(page, 'motion-' + reducedMotion)
      } finally { await context.close() }
    }
    expect(frames[0].y).toBe(frames[1].y); expect(frames[0].cursor).toEqual(frames[1].cursor)
    expect(frames[0].transform).toContain('rotate(0)'); expect(frames[1].transform).not.toContain('rotate(0)')
  })

  test('Flappy uses effects without adding a background-music control', async ({ page }) => {
    const h = await installApi(page)
    await open(page); await freezeClock(page); await begin(page); await button(page, 'Pause').click()
    const cursor = (await stored(page)).cursor
    await expect(page.locator('.game-ambient-toggle')).toHaveCount(0)
    await expect(page.locator('audio')).toHaveCount(0)
    await page.clock.runFor(1000)
    expect((await stored(page)).cursor).toEqual(cursor); expect(h.writes).toHaveLength(1)
    await expect(phase(page)).toHaveAttribute('data-phase', 'paused')
    await capture(page, 'effects-only-audio')
  })
})
