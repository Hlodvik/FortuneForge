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

const slotDemoPaths = [
  '/slots/wukong/demo',
  '/slots/rainbow-realm/demo',
  '/slots/pirates-fortune/demo',
  '/slots/gods-of-olympus/demo',
  '/slots/reel-riches/demo',
  '/slots/high-noon-fortune/demo',
  '/slots/royal-draw/demo',
  '/slots/arcane-archives/demo',
  '/slots/cosmic-fortune/demo',
  '/slots/dino-dominion/demo',
  '/slots/neon-nights/demo',
  '/slots/jungle-jackpot/demo',
  '/slots/ocean-odyssey/demo',
  '/slots/samurai-fortune/demo',
  '/slots/candy-carnival/demo',
  '/slots/phantom-manor/demo',
  '/slots/nordic-legends/demo',
  '/slots/desert-treasures/demo',
  '/slots/robot-revolution/demo',
  '/slots/dragon-hoard/demo',
] as const

test.beforeEach(async ({ page }) => {
  await page.emulateMedia({ colorScheme: 'dark', reducedMotion: 'reduce' })
})

test('idle cabinet', async ({ page }) => {
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
  await expect(page.locator('.slots-page__special-game-tally')).toBeVisible()

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

test('every slot cabinet fits a desktop viewport without document scrolling', async ({ page }) => {
  test.setTimeout(120_000)
  await page.setViewportSize({ width: 1366, height: 768 })
  await mockDemoApi(page)

  for (const path of slotDemoPaths) {
    await openStableSlot(page, path)
    await expect(page.locator('[data-game-navbar]'), path).toHaveCount(1)
    await expect(page.getByRole('link', { name: 'Fortune Forge home' }), path).toHaveAttribute('href', '/')
    await expect(page.getByRole('link', { name: 'Other Games' }), path).toHaveAttribute('href', '/demo')
    const metrics = await page.evaluate(() => {
      const machine = document.querySelector('.slot-game-frame')?.getBoundingClientRect()
      const spin = document.querySelector('.spin-button')?.getBoundingClientRect()
      window.scrollTo(0, 9999)
      return {
        documentHeight: document.documentElement.scrollHeight,
        documentWidth: document.documentElement.scrollWidth,
        machineBottom: machine?.bottom ?? Number.POSITIVE_INFINITY,
        machineTop: machine?.top ?? Number.NEGATIVE_INFINITY,
        scrollY: window.scrollY,
        spinBottom: spin?.bottom ?? Number.POSITIVE_INFINITY,
        spinTop: spin?.top ?? Number.NEGATIVE_INFINITY,
        viewportHeight: window.innerHeight,
        viewportWidth: window.innerWidth,
      }
    })

    expect(metrics, path).toMatchObject({ scrollY: 0 })
    expect(metrics.documentHeight, path).toBeLessThanOrEqual(metrics.viewportHeight + 1)
    expect(metrics.documentWidth, path).toBeLessThanOrEqual(metrics.viewportWidth + 1)
    expect(metrics.machineTop, path).toBeGreaterThanOrEqual(0)
    expect(metrics.machineBottom, path).toBeLessThanOrEqual(metrics.viewportHeight)
    expect(metrics.spinTop, path).toBeGreaterThanOrEqual(0)
    expect(metrics.spinBottom, path).toBeLessThanOrEqual(metrics.viewportHeight)
  }
})

test('every mobile slot keeps the complete playable cabinet in the first viewport', async ({ page }) => {
  test.setTimeout(120_000)
  await page.setViewportSize({ width: 390, height: 844 })
  await mockDemoApi(page)

  for (const path of slotDemoPaths) {
    await openStableSlot(page, path)
    const metrics = await page.evaluate(() => {
      const playbar = document.querySelector('.slots-page__playbar')?.getBoundingClientRect()
      return {
        documentWidth: document.documentElement.scrollWidth,
        playableBottom: playbar?.bottom ?? Number.POSITIVE_INFINITY,
        viewportWidth: window.innerWidth,
      }
    })
    expect(metrics.playableBottom, path).toBeLessThanOrEqual(844)
    expect(metrics.documentWidth, path).toBeLessThanOrEqual(metrics.viewportWidth + 1)
  }
})

test('Pirates controls, chest hints, and interaction guards stay intact', async ({ page }) => {
  await page.setViewportSize({ width: 1366, height: 768 })
  await mockDemoApi(page)
  await openStableSlot(page, '/slots/pirates-fortune/demo')

  await expect(page.locator('.slots-page__balance-label')).toHaveText('Balance')
  await expect(page.getByText('Demo balance', { exact: true })).toHaveCount(0)

  const chests = page.locator('.slots-page__seal-collection')
  const chestTops = await chests.locator('.slots-page__treasure-chest').evaluateAll((elements) => (
    elements.map((element) => Math.round(element.getBoundingClientRect().top))
  ))
  expect(new Set(chestTops).size).toBe(1)

  const chestLabels = await chests.locator('.slots-page__seal-title').evaluateAll((labels) => (
    labels.map((label) => {
      const style = getComputedStyle(label)
      return {
        horizontalOverflow: label.scrollWidth - label.clientWidth,
        overflow: style.overflow,
        textOverflow: style.textOverflow,
        verticalOverflow: label.scrollHeight - label.clientHeight,
      }
    })
  ))
  for (const label of chestLabels) {
    expect(label.horizontalOverflow).toBeLessThanOrEqual(1)
    expect(label.verticalOverflow).toBeLessThanOrEqual(1)
    expect(label.overflow).toBe('visible')
    expect(label.textOverflow).toBe('clip')
  }

  const navStyles = await page.getByRole('link', { name: 'Other Games' }).evaluate((link) => {
    const style = getComputedStyle(link)
    return { color: style.color, textDecoration: style.textDecorationLine }
  })
  expect(navStyles.color).not.toBe('rgb(0, 0, 238)')
  expect(navStyles.color).not.toBe('rgb(85, 26, 139)')
  expect(navStyles.textDecoration).toBe('none')

  await chests.nth(1).hover()
  const tooltip = chests.nth(1).locator('.slots-page__collection-tooltip')
  await expect(tooltip).toBeVisible()
  const tooltipBox = await tooltip.boundingBox()
  expect(tooltipBox?.y ?? -1).toBeGreaterThanOrEqual(0)
  expect((tooltipBox?.y ?? 9999) + (tooltipBox?.height ?? 9999)).toBeLessThanOrEqual(768)

  const helpButton = page.getByRole('button', { name: 'How to win' })
  const helpContainment = await helpButton.evaluate((button) => {
    const buttonBox = button.getBoundingClientRect()
    return Array.from(button.children).every((child) => {
      const childBox = child.getBoundingClientRect()
      return childBox.top >= buttonBox.top && childBox.bottom <= buttonBox.bottom
    })
  })
  expect(helpContainment).toBe(true)

  const wheel = await page.locator('.spin-button--pirate-helm').evaluate((button) => {
    const buttonBox = button.getBoundingClientRect()
    const iconBox = button.querySelector('.spin-button__icon')?.getBoundingClientRect()
    const style = getComputedStyle(button)
    return {
      backgroundImage: style.backgroundImage,
      borderWidth: style.borderTopWidth,
      iconRatio: iconBox ? iconBox.width / buttonBox.width : 0,
    }
  })
  expect(wheel.backgroundImage).toBe('none')
  expect(wheel.borderWidth).toBe('0px')
  expect(wheel.iconRatio).toBeGreaterThan(0.9)

  expect(await page.locator('.slots-page__special-round').count()).toBe(0)
  expect(await page.locator('body').evaluate((body) => getComputedStyle(body).userSelect)).toBe('none')
  expect(await page.locator('.slots-page__treasure-chest-art').first().getAttribute('draggable')).toBe('false')
  const nativeDragAllowed = await page.locator('.slot-symbol__image').first().evaluate((image) => (
    image.dispatchEvent(new DragEvent('dragstart', { bubbles: true, cancelable: true }))
  ))
  expect(nativeDragAllowed).toBe(false)
})

test('legacy saved mute state no longer makes a slot launch muted', async ({ page }) => {
  await page.addInitScript(() => {
    window.localStorage.setItem('fortune-forge.audio-preferences', JSON.stringify({ mode: 'muted', volume: 20 }))
    window.localStorage.removeItem('fortune-forge.audio-preferences.v2')
  })
  await mockDemoApi(page)
  await openStableSlot(page, '/slots/pirates-fortune/demo')
  await page.getByRole('button', { name: 'Open settings' }).click()

  await expect(page.getByRole('button', { name: /Mute Turn off all game audio/i })).toHaveAttribute('aria-pressed', 'false')
  await expect(page.getByRole('button', { name: /Mute all but result sounds/i })).toHaveAttribute('aria-pressed', 'false')
  await expect(page.locator('.audio-settings__volume-heading output')).toHaveText('20%')
})

async function openStableSlot(page: Page, path: string) {
  await page.goto(path)
  await expect(page.getByRole('button', { name: 'Spin the reels' })).toBeEnabled()
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
