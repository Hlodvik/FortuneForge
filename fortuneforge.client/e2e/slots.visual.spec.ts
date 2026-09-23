import { expect, test, type Page } from '@playwright/test'

type SpinRequest = {
  gameId: string
  wagerPoints: number
  useFreeSpin: boolean
  freeSpinsRemaining: number
  freeSpinWagerPoints: number | null
}

type SpinKind = 'bonus' | 'loss' | 'win'

type DemoApiOptions = {
  kind?: SpinKind
  onFreeSpinRequest?: () => Promise<void>
}

const reelRows = [
  ['2', '3', '4', '5'],
  ['3', '4', '5', '6'],
  ['4', '5', '6', '7'],
  ['5', '6', '7', 'ACE'],
  ['6', '7', 'ACE', 'POWER'],
]

test.beforeEach(async ({ page }) => {
  await page.emulateMedia({ colorScheme: 'dark', reducedMotion: 'reduce' })
})

test('idle cabinet and persistent feature path', async ({ page }) => {
  await mockDemoApi(page)
  await openStableSlot(page, '/slots/reel-riches/demo')

  await expect(page).toHaveScreenshot('idle-reel-riches-cabinet.png')
})

test('rules and payline guide', async ({ page }) => {
  await mockDemoApi(page)
  await openStableSlot(page, '/slots/pirates-fortune/demo')
  await page.getByRole('button', { name: 'How to win' }).click()
  await expect(page.getByRole('dialog', { name: 'What counts as a win?' })).toBeVisible()

  await expect(page).toHaveScreenshot('rules-and-paylines.png')
})

test('feature transition with deterministic free spins', async ({ page }) => {
  const featureGate = deferred()
  await mockDemoApi(page, {
    kind: 'bonus',
    onFreeSpinRequest: async () => {
      featureGate.started()
      await featureGate.releasePromise
    },
  })
  await openStableSlot(page, '/slots/gods-of-olympus/demo')

  await page.getByRole('button', { name: 'Spin the reels' }).click()
  await expect(page.locator('.slots-page__free-spin-badge')).toBeVisible()
  await expect(page.getByRole('button', { name: 'Spin the reels' })).toBeEnabled()
  await page.getByRole('button', { name: 'Spin the reels' }).click()
  await featureGate.startedPromise
  await expect(page.locator('[data-feature-state="active"]')).toBeVisible()

  await expect(page.locator('.slots-page__stage')).toHaveScreenshot('active-olympian-feature.png')
  featureGate.release()
})

test('settled win with highlighted payline and outcome', async ({ page }) => {
  await mockDemoApi(page, { kind: 'win' })
  await openStableSlot(page, '/slots/robot-revolution/demo')
  await page.getByRole('button', { name: 'Spin the reels' }).click()
  await expect(page.locator('.game-outcome-banner--win')).toBeVisible()

  await expect(page.locator('.slots-page')).toHaveScreenshot('settled-robot-win.png')
})

test('service failure state', async ({ page }) => {
  await mockDemoApi(page, { kind: 'loss' })
  await page.route('**/api/slots/demo/spins', async (route) => {
    await route.fulfill({
      status: 503,
      contentType: 'application/json',
      body: JSON.stringify({ error: 'visual-test outage' }),
    })
  })
  await openStableSlot(page, '/slots/wukong/demo')
  await page.getByRole('button', { name: 'Spin the reels' }).click()
  await expect(page.locator('.slots-page__footer--error')).toBeVisible()

  await expect(page.locator('.slots-page')).toHaveScreenshot('demo-service-error.png')
})

test('narrow mobile cabinet remains playable', async ({ page }) => {
  await page.setViewportSize({ width: 390, height: 844 })
  await mockDemoApi(page)
  await openStableSlot(page, '/slots/samurai-fortune/demo')

  await expect(page.getByRole('button', { name: 'Spin the reels' })).toBeInViewport()
  await expect(page.locator('.slots-page')).toHaveScreenshot('narrow-mobile-samurai.png')
})

async function openStableSlot(page: Page, path: string) {
  await page.goto(path)
  await expect(page.getByRole('button', { name: 'Spin the reels' })).toBeEnabled()
  await page.waitForLoadState('networkidle')
  await page.evaluate(async () => {
    await document.fonts.ready
    const images = Array.from(document.images)
    await Promise.all(images.map((image) => image.complete
      ? Promise.resolve()
      : new Promise<void>((resolve) => {
          image.addEventListener('load', () => resolve(), { once: true })
          image.addEventListener('error', () => resolve(), { once: true })
        })))
  })
  await page.addStyleTag({ content: `
    *, *::before, *::after {
      caret-color: transparent !important;
      scroll-behavior: auto !important;
    }

  ` })
}

async function mockDemoApi(page: Page, options: DemoApiOptions = {}) {
  await page.route('**/api/slots/demo/status?**', async (route) => {
    await route.fulfill({ status: 204 })
  })
  await page.route('**/api/slots/demo/spins', async (route) => {
    const request = route.request().postDataJSON() as SpinRequest
    if (request.useFreeSpin && options.onFreeSpinRequest) {
      await options.onFreeSpinRequest()
    }
    const kind = request.useFreeSpin ? 'loss' : options.kind ?? 'loss'
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify(spinFixture(request, kind)),
    })
  })
}

function spinFixture(request: SpinRequest, kind: SpinKind) {
  const isBonus = kind === 'bonus'
  const isWin = kind === 'win'
  const isFreeSpin = request.useFreeSpin
  const reels = isBonus
    ? reelRows.map((reel, index) => index < 3 ? ['FREE', ...reel.slice(1)] : [...reel])
    : isWin
      ? reelRows.map((reel) => ['7', ...reel.slice(1)])
      : reelRows.map((reel) => [...reel])
  const positions = reels.map((_, reel) => ({ reel, row: 0 }))
  const remainingFreeSpins = isBonus
    ? 6
    : isFreeSpin
      ? Math.max(0, request.freeSpinsRemaining - 1)
      : 0
  const featureMode = isBonus || isFreeSpin ? 'paw-rand' : null

  return {
    spinId: '00000000-0000-4000-8000-000000000001',
    gameId: request.gameId,
    reelSetId: 'visual-fixture-reels-v1',
    symbolSetId: 'wukong-treasures-v3',
    paytableId: 'visual-fixture-paytable-v1',
    wagerPoints: request.wagerPoints,
    pointValueInCents: 25,
    reelStops: [0, 1, 2, 3, 4],
    reels,
    consecutiveFiveMisses: 0,
    fiveMatchPityTriggered: false,
    isFreeSpin,
    freeSpinsAwarded: isBonus ? 6 : 0,
    freeSpinsRemaining: remainingFreeSpins,
    freeSpinWagerPoints: remainingFreeSpins > 0
      ? request.freeSpinWagerPoints ?? request.wagerPoints
      : null,
    specialPointsAwarded: 0,
    specialPointsBalance: 0,
    energyAwarded: 0,
    energyBalance: 0,
    energyMultiplierApplied: false,
    payoutMultiplier: 1,
    monkeyPawCount: 0,
    moneyGrabPoints: 0,
    bananaBonusPoints: 0,
    sealsAwarded: {},
    sealCollections: [
      { sealId: 'sync', count: 14, averageWagerPoints: request.wagerPoints, requiredCount: 28 },
      { sealId: 'rows', count: 8, averageWagerPoints: request.wagerPoints, requiredCount: 28 },
      { sealId: 'paw', count: 21, averageWagerPoints: request.wagerPoints, requiredCount: 28 },
      { sealId: 'rand', count: 4, averageWagerPoints: request.wagerPoints, requiredCount: 28 },
    ],
    freeSpinFeatureMode: featureMode,
    specialBoostApplied: false,
    slotsCreditsBalance: null,
    payout: isWin
      ? {
          totalPoints: 8,
          paylines: [{
            paylineId: 1,
            amountPoints: 8,
            matches: [{
              multiplier: 4,
              amountPoints: 8,
              match: {
                paylineId: 1,
                symbolId: '7',
                matchLength: 5,
                positions,
                wildPositions: [],
              },
            }],
          }],
        }
      : { totalPoints: 0, paylines: [] },
  }
}

function deferred() {
  let release!: () => void
  let started!: () => void
  const releasePromise = new Promise<void>((resolve) => { release = resolve })
  const startedPromise = new Promise<void>((resolve) => { started = resolve })
  return { release, releasePromise, started, startedPromise }
}
