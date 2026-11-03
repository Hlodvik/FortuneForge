import { expect, test, type Page } from '@playwright/test'

type SpinRequest = {
  gameId: string
  wagerPoints: number
  useFreeSpin: boolean
  freeSpinsRemaining: number
  freeSpinWagerPoints: number | null
  freeSpinFeatureMode?: string | null
}

type SpinKind = 'bonus' | 'collection' | 'loss' | 'win'

type DemoApiOptions = {
  kind?: SpinKind
  bonusSpinCount?: number
  freeSpinPayoutPoints?: number
  onFreeSpinRequest?: () => Promise<void>
  onSpinRequest?: () => Promise<void>
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

test('Reel Riches bonus entry and active casts remain playable on desktop and mobile', async ({ page }) => {
  test.setTimeout(90_000)
  await mockDemoApi(page, { kind: 'bonus' })

  for (const viewport of [{ width: 1366, height: 768 }, { width: 390, height: 844 }]) {
    await page.setViewportSize(viewport)
    await openStableSlot(page, '/slots/reel-riches/demo')
    await page.getByRole('button', { name: 'Spin the reels' }).click()

    const intro = page.locator('.reel-riches__bonus-intro')
    await expect(intro).toBeVisible()
    await expect(intro).toContainText('Trophy Catch')
    const introBox = await intro.boundingBox()
    expect(introBox?.x ?? -1).toBeGreaterThanOrEqual(0)
    expect((introBox?.x ?? 9999) + (introBox?.width ?? 9999)).toBeLessThanOrEqual(viewport.width)
    expect(introBox?.y ?? -1).toBeGreaterThanOrEqual(0)
    expect((introBox?.y ?? 9999) + (introBox?.height ?? 9999)).toBeLessThanOrEqual(viewport.height)
    await expect(page.locator('.slots-page')).toHaveScreenshot(`reel-riches-bonus-intro-${viewport.width}.png`)

    await page.getByRole('button', { name: 'Spin the reels' }).click()
    await expect(intro).toHaveCount(0)
    await expect(page.locator('.reel-riches__bonus-hud')).toBeVisible()
    await expect(page.locator('.slots-page--special-game')).toBeVisible()
    await expect(page.getByRole('button', { name: 'Spin the reels' })).toBeEnabled()
    await expect(page.locator('.slots-page')).toHaveScreenshot(`reel-riches-active-casts-${viewport.width}.png`)
    const metrics = await page.evaluate(() => ({
      width: document.documentElement.scrollWidth,
      spinBottom: document.querySelector('.spin-button')?.getBoundingClientRect().bottom ?? Infinity,
    }))
    expect(metrics.width).toBeLessThanOrEqual(viewport.width + 1)
    expect(metrics.spinBottom).toBeLessThanOrEqual(viewport.height)
  }
})

test('Reel Riches badge collection opens the legendary feature', async ({ page }) => {
  await mockDemoApi(page, { kind: 'collection' })
  await openStableSlot(page, '/slots/reel-riches/demo')
  await page.getByRole('button', { name: 'Spin the reels' }).click()

  const intro = page.locator('.reel-riches__bonus-intro')
  await expect(intro).toBeVisible()
  await expect(intro).toContainText('Legendary Catch')
  await expect(intro).toContainText('10 free casts')
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

test('Rainbow Realm Orchard Rush remains playable on desktop and phone', async ({ page }, testInfo) => {
  await mockDemoApi(page, { kind: 'bonus' })

  for (const viewport of [{ width: 1366, height: 768 }, { width: 390, height: 844 }]) {
    await page.setViewportSize(viewport)
    await openStableSlot(page, '/slots/rainbow-realm/demo')
    await expect(page.locator('.slot-reel [data-symbol="FREE"] .slot-symbol__image').first())
      .toHaveAttribute('src', /orchard-rush-rainbow-v1-192\.webp/)
    const pageArt = viewport.width > viewport.height
      ? 'rainbow-realm-sunrise-valley-page-v1.webp'
      : 'rainbow-realm-sunrise-valley-page-portrait-v1.webp'
    const cabinet = page.locator('.slot-game-frame__visuals')
    await expect(page.locator('.slots-page')).toHaveCSS('background-image', new RegExp(pageArt))
    await expect(cabinet).toHaveCSS('background-image', /radial-gradient\(/)
    await expect(cabinet).not.toHaveCSS('background-image', /url\(/)
    const baseCabinetBackground = await cabinet.evaluate(element => getComputedStyle(element).backgroundImage)
    await page.getByRole('button', { name: 'Spin the reels' }).click()
    await expect(page.getByRole('button', { name: 'Spin the reels' })).toBeEnabled()
    await expect(page.locator('.rainbow-realm__bonus-intro')).toContainText('10 bonus spins are ready')
    await expect(page.locator('.rainbow-realm__bonus-intro > img'))
      .toHaveAttribute('src', /orchard-rush-rainbow-v1-320\.webp/)
    await expect(page.locator('[data-rainbow-bonus="true"]')).toBeVisible()
    await page.screenshot({ path: testInfo.outputPath(`rainbow-orchard-intro-${viewport.width}.png`), fullPage: true })
    await page.getByRole('button', { name: 'Spin the reels' }).click()
    await expect(page.locator('.rainbow-realm__bonus-intro')).toHaveCount(0)
    await expect(page.locator('.slots-page--special-game')).toBeVisible()
    await expect(page.locator('.slots-page__special-game-tally')).toBeVisible()
    await expect(page.locator('.slots-page__special-game-tally')).toContainText('bonus spins left')

    const metrics = await page.evaluate(() => {
      const spin = document.querySelector('.spin-button')?.getBoundingClientRect()
      const page = document.querySelector('.slots-page')
      return {
        background: page ? getComputedStyle(page).backgroundImage : '',
        documentWidth: document.documentElement.scrollWidth,
        spinBottom: spin?.bottom ?? Number.POSITIVE_INFINITY,
        spinTop: spin?.top ?? Number.NEGATIVE_INFINITY,
      }
    })
    await page.screenshot({ path: testInfo.outputPath(`rainbow-orchard-rush-${viewport.width}.png`), fullPage: true })
    expect(metrics.background).toContain(pageArt)
    await expect(cabinet).not.toHaveCSS('background-image', baseCabinetBackground)
    await expect(cabinet).not.toHaveCSS('background-image', /url\(/)
    expect(metrics.documentWidth).toBeLessThanOrEqual(viewport.width + 1)
    expect(metrics.spinTop).toBeGreaterThanOrEqual(0)
    expect(metrics.spinBottom).toBeLessThanOrEqual(viewport.height)

  }
})

test('Rainbow Realm filled glass shows its enhanced Orchard Rush mode', async ({ page }) => {
  await mockDemoApi(page, { kind: 'collection' })
  await openStableSlot(page, '/slots/rainbow-realm/demo')
  await page.getByRole('button', { name: 'Spin the reels' }).click()

  const intro = page.locator('.rainbow-realm__bonus-intro')
  await expect(intro).toContainText('Orchard glass filled')
  await expect(intro).toContainText('10 bonus spins are ready')
  await expect(intro).toContainText('Strawberry Sync')
})

test('Rainbow Realm keeps the completed bonus total until the player continues', async ({ page }) => {
  test.setTimeout(45_000)
  await mockDemoApi(page, { kind: 'bonus', bonusSpinCount: 2, freeSpinPayoutPoints: 8 })
  await openStableSlot(page, '/slots/rainbow-realm/demo')
  await page.getByRole('button', { name: 'Spin the reels' }).click()
  await expect(page.locator('.rainbow-realm__bonus-intro')).toContainText('2 bonus spins are ready')
  await page.getByRole('button', { name: 'Spin the reels' }).click()
  await expect(page.locator('.slots-page__special-game-tally')).toContainText('1 bonus spin left')
  await page.getByRole('button', { name: 'Spin the reels' }).click()

  const summary = page.getByRole('dialog', { name: 'Orchard Rush' })
  await expect(summary).toBeVisible()
  await expect(summary).toContainText('Bonus win R4')
  await expect(page.getByRole('button', { name: 'Spin the reels' })).toBeDisabled()
  await expect(page.locator('[data-rainbow-bonus="true"]')).toBeVisible()

  await summary.getByRole('button', { name: 'Continue' }).click()
  await expect(summary).toHaveCount(0)
  await expect(page.getByRole('button', { name: 'Spin the reels' })).toBeEnabled()
  await expect(page.locator('[data-rainbow-bonus="true"]')).toHaveCount(0)
})

test('Rainbow Realm glass entry preserves its bonus total without a scatter award', async ({ page }) => {
  await mockDemoApi(page, { kind: 'collection', bonusSpinCount: 1, freeSpinPayoutPoints: 8 })
  await openStableSlot(page, '/slots/rainbow-realm/demo')
  await page.getByRole('button', { name: 'Spin the reels' }).click()
  await expect(page.locator('.rainbow-realm__bonus-intro')).toContainText('Orchard glass filled')
  await page.getByRole('button', { name: 'Spin the reels' }).click()
  await expect(page.getByRole('dialog', { name: 'Orchard Rush' })).toContainText('Bonus win R2')
})

test('Rainbow Realm and Pirates use size-matched reel artwork', async ({ page }) => {
  await page.emulateMedia({ reducedMotion: 'no-preference' })
  await mockDemoApi(page)

  for (const path of ['/slots/rainbow-realm/demo', '/slots/pirates-fortune/demo']) {
    for (const viewport of [{ width: 390, height: 844 }, { width: 1366, height: 768 }]) {
      await page.setViewportSize(viewport)
      await openStableSlot(page, path)
      const reelImages = await page.locator('.slot-reel .slot-symbol__image').evaluateAll((images) =>
        images.map((image) => ({
          source: (image as HTMLImageElement).currentSrc,
          candidates: (image as HTMLImageElement).srcset,
        })),
      )
      expect(reelImages.length).toBeGreaterThan(0)
      for (const image of reelImages) {
        expect(image.source).toContain('/responsive/')
        expect(image.source).toMatch(/\.webp(?:\?|$)/)
        expect(image.candidates).toContain('96w')
        expect(image.candidates).toContain('192w')
        expect(image.candidates).toMatch(/(?:256|320)w/)
      }

      if (path.includes('rainbow') && viewport.width === 390) {
        const orange = page.locator('.slot-reel [data-symbol="3"] .slot-symbol__image').first()
        await expect(orange).toBeVisible()
        const firstFrame = await orange.screenshot({ animations: 'allow' })
        await page.waitForTimeout(240)
        const nextFrame = await orange.screenshot({ animations: 'allow' })
        expect(firstFrame.equals(nextFrame)).toBe(false)
      }
    }
  }
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

test('Wukong keeps its celestial HUD, centred reels, and spin crest separated', async ({ page }) => {
  test.setTimeout(90_000)
  await mockDemoApi(page, { kind: 'loss' })

  for (const viewport of [
    { width: 1280, height: 720 },
    { width: 1366, height: 768 },
    { width: 1440, height: 900 },
  ]) {
    await page.setViewportSize(viewport)
    await openStableSlot(page, '/slots/wukong/demo')
    await expect(page.locator('.app-shell__background-video')).toHaveCount(0)
    await expect(page.locator('[data-symbol="FREE"]')).toHaveCount(0)
    await expect(page.locator('[data-symbol="PAW"]')).toHaveCount(1)

    const idleMetrics = await page.evaluate(() => {
      const frame = document.querySelector('.slot-game-frame')?.getBoundingClientRect()
      const reels = document.querySelector('.slot-machine__reels')?.getBoundingClientRect()
      const energy = document.querySelector('.slots-page__energy-meter')?.getBoundingClientRect()
      const spin = document.querySelector('.spin-button')?.getBoundingClientRect()
      const spinIcon = document.querySelector('.spin-button__icon')?.getBoundingClientRect()
      const spinLabel = document.querySelector('.spin-button__label')?.getBoundingClientRect()
      const stage = document.querySelector('.slots-page__stage')?.getBoundingClientRect()
      const orbitBottoms = Array.from(document.querySelectorAll('.slots-page__collection-orbit'))
        .map((element) => element.getBoundingClientRect().bottom)
      return {
        energyTop: energy?.top ?? Number.NEGATIVE_INFINITY,
        frameLeftInset: frame && reels ? reels.left - frame.left : Number.POSITIVE_INFINITY,
        frameRightInset: frame && reels ? frame.right - reels.right : Number.NEGATIVE_INFINITY,
        iconBottom: spinIcon?.bottom ?? Number.POSITIVE_INFINITY,
        labelTop: spinLabel?.top ?? Number.NEGATIVE_INFINITY,
        maxOrbitBottom: Math.max(...orbitBottoms),
        spinWidth: spin?.width ?? 0,
        stageWidth: stage?.width ?? 0,
      }
    })

    expect(Math.abs(idleMetrics.frameLeftInset - idleMetrics.frameRightInset), JSON.stringify(viewport))
      .toBeLessThanOrEqual(1)
    expect(idleMetrics.maxOrbitBottom, JSON.stringify(viewport)).toBeLessThanOrEqual(idleMetrics.energyTop)
    expect(idleMetrics.spinWidth, JSON.stringify(viewport)).toBeGreaterThanOrEqual(88)
    expect(idleMetrics.iconBottom, JSON.stringify(viewport)).toBeLessThanOrEqual(idleMetrics.labelTop + 1)
    expect(idleMetrics.stageWidth, JSON.stringify(viewport)).toBeGreaterThanOrEqual(540)

    const meterLabels = await page.locator('.slots-page__seal-title').evaluateAll((labels) => (
      labels.map((label) => label.scrollWidth - label.clientWidth)
    ))
    expect(Math.max(...meterLabels), JSON.stringify(viewport)).toBeLessThanOrEqual(1)

    const pawMeter = page.locator('.slots-page__seal-collection--paw')
    await pawMeter.hover()
    const pawTooltip = pawMeter.locator('.slots-page__collection-tooltip')
    await expect(pawTooltip).toContainText('add 2–5 monkey paws to every spin')
    const tooltipBox = await pawTooltip.boundingBox()
    expect(tooltipBox?.y ?? -1, JSON.stringify(viewport)).toBeGreaterThanOrEqual(0)
    expect((tooltipBox?.y ?? 9999) + (tooltipBox?.height ?? 9999), JSON.stringify(viewport))
      .toBeLessThanOrEqual(viewport.height)

    await page.getByRole('button', { name: 'Spin the reels' }).click()
    await expect(page.locator('.slots-page__footer')).toHaveCount(0)
    const settledMetrics = await page.evaluate(() => {
      return {
        documentHeight: document.documentElement.scrollHeight,
        viewportHeight: window.innerHeight,
      }
    })
    expect(settledMetrics.documentHeight, JSON.stringify(viewport))
      .toBeLessThanOrEqual(settledMetrics.viewportHeight + 1)
  }
})

test('ordinary Wukong and Pirates spins do not mount a status panel', async ({ page }) => {
  let spinGate = deferred()
  await mockDemoApi(page, {
    onSpinRequest: async () => {
      spinGate.started()
      await spinGate.releasePromise
    },
  })

  for (const path of ['/slots/wukong/demo', '/slots/pirates-fortune/demo']) {
    await openStableSlot(page, path)
    await page.getByRole('button', { name: 'Spin the reels' }).click()
    await spinGate.startedPromise
    await expect(page.getByRole('button', { name: 'Stop the spin' })).toBeVisible()
    await expect(page.locator('.slots-page__footer')).toHaveCount(0)
    spinGate.release()
    await expect(page.getByRole('button', { name: 'Spin the reels' })).toBeEnabled()
    await expect(page.locator('.slots-page__footer')).toHaveCount(0)
    spinGate = deferred()
  }
})

test('Pirates controls, gem hints, and interaction guards stay intact', async ({ page }) => {
  await page.setViewportSize({ width: 1366, height: 768 })
  await mockDemoApi(page)
  await openStableSlot(page, '/slots/pirates-fortune/demo')

  await expect(page.locator('.slots-page__balance-label')).toHaveText('Balance')
  await expect(page.getByText('Demo balance', { exact: true })).toHaveCount(0)

  const collections = page.locator('.slots-page__seal-collection')
  const pileTops = await collections.locator('.slots-page__pirate-gem-pile').evaluateAll((elements) => (
    elements.map((element) => Math.round(element.getBoundingClientRect().top))
  ))
  expect(pileTops).toHaveLength(4)
  expect(new Set(pileTops).size).toBe(1)

  const gemLabels = await collections.locator('.slots-page__seal-title').evaluateAll((labels) => (
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
  for (const label of gemLabels) {
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

  await collections.nth(1).hover()
  const tooltip = collections.nth(1).locator('.slots-page__collection-tooltip')
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
  expect(await page.locator('.slots-page__pirate-gem-pile img').first().getAttribute('draggable')).toBe('false')
  await expect(page.locator('.slots-page__treasure-chest-art')).toHaveCount(0)
  const nativeDragAllowed = await page.locator('.slot-symbol__image').first().evaluate((image) => (
    image.dispatchEvent(new DragEvent('dragstart', { bubbles: true, cancelable: true }))
  ))
  expect(nativeDragAllowed).toBe(false)
})

test('Pirates gem rail remains legible on desktop and mobile', async ({ page }) => {
  await mockDemoApi(page)
  for (const viewport of [{ width: 1366, height: 768 }, { width: 390, height: 844 }]) {
    await page.setViewportSize(viewport)
    await openStableSlot(page, '/slots/pirates-fortune/demo')
    await page.getByRole('button', { name: 'Spin the reels' }).click()
    await expect(page.getByRole('button', { name: 'Spin the reels' })).toBeEnabled()

    const rail = page.locator('.slots-page__seal-collections')
    const metrics = await rail.evaluate((element) => {
      const railRect = element.getBoundingClientRect()
      const labels = Array.from(element.querySelectorAll('.slots-page__seal-title'))
      const meters = Array.from(element.querySelectorAll('.slots-page__pirate-gem-meter'))
      return {
        railLeft: railRect.left,
        railRight: railRect.right,
        labelOverflow: Math.max(...labels.map((label) => label.scrollWidth - label.clientWidth)),
        meterCount: meters.length,
        minFontSize: Math.min(...labels.map((label) => Number.parseFloat(getComputedStyle(label).fontSize))),
      }
    })
    expect(metrics.railLeft).toBeGreaterThanOrEqual(0)
    expect(metrics.railRight).toBeLessThanOrEqual(viewport.width)
    expect(metrics.labelOverflow).toBeLessThanOrEqual(1)
    expect(metrics.meterCount).toBe(4)
    expect(metrics.minFontSize).toBeGreaterThanOrEqual(8)
    await expect(rail).toHaveScreenshot(`pirates-gem-rail-${viewport.width}.png`)
  }
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

test('Gold Foundry credits its crucible payout and resets the fill', async ({ page }) => {
  const response = spinFixture({
    gameId: 'dino-dominion-v1',
    wagerPoints: 2,
    useFreeSpin: false,
    freeSpinsRemaining: 0,
    freeSpinWagerPoints: null,
  }, 'loss')
  const openingCharge = 99
  const partialCharge = {
    ...response,
    spinId: '00000000-0000-4000-8000-000000000001',
    payout: { totalPoints: 0, paylines: [] },
    monkeyPawCount: 0,
    moneyGrabPoints: 0,
    freeSpinsRemaining: 0,
    freeSpinWagerPoints: null,
    freeSpinFeatureMode: null,
    reels: response.reels.map((reel, index) => index === 2 ? ['BOLT', ...reel.slice(1)] : reel),
    energyAwarded: openingCharge,
    energyBalance: openingCharge,
    energyMultiplierApplied: false,
    payoutMultiplier: 1,
    sealCollections: [],
    crucibleWageredCents: 100_000,
    cruciblePourValueCents: 15_000,
    cruciblePoured: false,
    cruciblePayoutCents: 0,
  }
  const fullCharge = {
    ...partialCharge,
    spinId: '00000000-0000-4000-8000-000000000002',
    energyAwarded: 1,
    energyBalance: 0,
    crucibleWageredCents: 0,
    cruciblePourValueCents: 0,
    cruciblePoured: true,
    cruciblePayoutCents: 15_007,
  }
  await page.route('**/api/slots/demo/status?**', (route) => route.fulfill({ status: 204 }))
  await page.route('**/api/slots/demo/spins', (route) => {
    const { energyBalance, crucibleWageredCents } = route.request().postDataJSON() as {
      energyBalance: number
      crucibleWageredCents: number
    }
    expect(crucibleWageredCents).toBe(energyBalance === 0 ? 0 : partialCharge.crucibleWageredCents)
    return route.fulfill({ json: energyBalance === 0 ? partialCharge : fullCharge })
  })

  for (const { width, height, reducedMotion } of [
    { width: 1366, height: 768, reducedMotion: 'no-preference' },
    { width: 390, height: 844, reducedMotion: 'reduce' },
  ] as const) {
    await page.setViewportSize({ width, height })
    await page.emulateMedia({ reducedMotion })
    await openStableSlot(page, '/slots/dino-dominion/demo')

    const crucible = page.getByRole('progressbar', { name: 'Gold crucible' })
    const pourValue = page.locator('.slots-page__crucible-copy strong')
    const spin = page.getByRole('button', { name: 'Spin the reels' })
    await expect(crucible.locator('.gold-crucible')).toBeVisible()
    await expect(page.locator('.slots-page__energy-track')).toHaveCount(0)
    await expect(crucible).toHaveAttribute('aria-valuenow', '0')
    await expect(crucible).toBeInViewport()
    expect(await page.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(width + 1)

    await spin.click()
    await expect(crucible).toHaveAttribute('aria-valuenow', String(openingCharge))
    await expect(pourValue).toHaveText('R150.00')
    await expect(crucible).not.toHaveAttribute('data-pouring', 'true')
    await expect(spin).toBeEnabled()

    await spin.click()
    if (reducedMotion === 'no-preference') {
      await expect(crucible).toHaveAttribute('data-pouring', 'true')
      await expect(crucible).toHaveAttribute('aria-valuenow', '100')
    }
    await expect(crucible).toHaveAttribute('aria-valuenow', String(fullCharge.energyBalance))
    await expect(crucible).not.toHaveAttribute('data-pouring', 'true')
    await expect(pourValue).toHaveText('R0.00')
    await expect(page.getByRole('status').filter({ hasText: 'R150.07 credited' })).toBeVisible()
    await expect(page.locator('.slots-page__free-spin-badge')).toHaveCount(0)
    await expect(page.getByLabel('Balance R10,149.07', { exact: true })).toBeVisible()
  }
})

test('Gold Foundry shows a reel crucible payout separately from paylines', async ({ page }) => {
  await page.route('**/api/slots/demo/status?**', (route) => route.fulfill({ status: 204 }))
  await page.route('**/api/slots/demo/spins', (route) => {
    const request = route.request().postDataJSON() as SpinRequest
    return route.fulfill({
      json: {
        ...spinFixture(request, 'loss'),
        reels: [
          ['PAW', '2', '3', '4'],
          ['5', '6', 'SEAL_ROWS', '2'],
          ['7', '2', '3', '5'],
          ['3', '5', '6', 'SEAL_RAND'],
          ['2', '3', '4', '5'],
        ],
        monkeyPawCount: 1,
        moneyGrabPoints: 5,
        sealCollections: [],
        payout: { totalPoints: 5, paylines: [] },
      },
    })
  })

  await openStableSlot(page, '/slots/dino-dominion/demo')
  await expect(page.locator('.slots-page__seal-collections')).toHaveCount(0)
  await page.getByRole('button', { name: 'Spin the reels' }).click()
  await expect(page.getByText('Ingot haul', { exact: true })).toBeVisible()
  await expect(page.getByText('+R1.25', { exact: true })).toBeVisible()
  await expect(page.getByLabel('Balance R10,000.75', { exact: true })).toBeVisible()
  await expect(page.getByText('Line win', { exact: true })).toHaveCount(0)
  await expect(page.locator('.slots-page__free-spin-badge')).toHaveCount(0)
})

test('Wukong keeps its energy boost and ignores Foundry payout fields', async ({ page }) => {
  const crucibleAssetRequests: string[] = []
  page.on('request', (request) => {
    if (request.url().includes('gold-foundry-crucible-stages')) crucibleAssetRequests.push(request.url())
  })
  await page.route('**/api/slots/demo/status?**', (route) => route.fulfill({ status: 204 }))
  await page.route('**/api/slots/demo/spins', (route) => {
    const request = route.request().postDataJSON() as SpinRequest
    expect(request).not.toHaveProperty('crucibleWageredCents')
    return route.fulfill({
      json: {
        ...spinFixture(request, 'win'),
        energyMultiplierApplied: true,
        payoutMultiplier: 1.5,
        cruciblePoured: true,
        cruciblePayoutCents: 15_000,
      },
    })
  })

  await openStableSlot(page, '/slots/wukong/demo')
  await expect(page.locator('.slots-page__energy-track')).toHaveCount(1)
  await expect(page.locator('.gold-crucible')).toHaveCount(0)
  await page.getByRole('button', { name: 'Spin the reels' }).click()
  await expect(page.getByText(/boost ×1\.5/)).toBeVisible()
  await expect(page.getByLabel('Balance R10,001.50', { exact: true })).toBeVisible()
  expect(crucibleAssetRequests).toEqual([])
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
    if (options.onSpinRequest) {
      await options.onSpinRequest()
    }
    if (request.useFreeSpin && options.onFreeSpinRequest) {
      await options.onFreeSpinRequest()
    }
    const kind = request.useFreeSpin && options.freeSpinPayoutPoints
      ? 'win'
      : request.useFreeSpin ? 'loss' : options.kind ?? 'loss'
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify(spinFixture(request, kind, options)),
    })
  })
}

function spinFixture(request: SpinRequest, kind: SpinKind, options: DemoApiOptions = {}) {
  const isBonus = kind === 'bonus'
  const isCollection = kind === 'collection'
  const isWin = kind === 'win'
  const isFreeSpin = request.useFreeSpin
  const isRainbowRealm = request.gameId === 'rainbow-realm-fruits-v1'
  const freeSpinsAwarded = isRainbowRealm ? options.bonusSpinCount ?? 10 : request.gameId === 'reel-riches-v1' ? 5 : 6
  const payoutPoints = isFreeSpin ? options.freeSpinPayoutPoints ?? 0 : isWin ? 8 : 0
  const reels = isBonus
    ? reelRows.map((reel, index) => index < 3 ? ['FREE', ...reel.slice(1)] : [...reel])
    : isWin
      ? reelRows.map((reel) => ['7', ...reel.slice(1)])
      : reelRows.map((reel) => [...reel])
  const positions = reels.map((_, reel) => ({ reel, row: 0 }))
  const remainingFreeSpins = isBonus
    ? freeSpinsAwarded
    : isCollection
      ? isRainbowRealm ? options.bonusSpinCount ?? 10 : 10
    : isFreeSpin
      ? Math.max(0, request.freeSpinsRemaining - 1)
      : 0
  const featureMode = isCollection
    ? isRainbowRealm ? 'sync' : 'sync-rows-rand'
    : isFreeSpin
      ? request.freeSpinFeatureMode ?? (isRainbowRealm || request.gameId === 'reel-riches-v1' ? null : 'paw-rand')
      : isBonus && request.gameId !== 'reel-riches-v1' && !isRainbowRealm
        ? 'paw-rand'
        : null

  return {
    spinId: '00000000-0000-4000-8000-000000000001',
    gameId: request.gameId,
    reelSetId: 'visual-fixture-reels-v1',
    symbolSetId: isRainbowRealm ? 'rainbow-realm-symbols-v1'
      : request.gameId === 'dino-dominion-v1' ? 'gold-foundry-symbols-v1' : 'wukong-treasures-v3',
    paytableId: 'visual-fixture-paytable-v1',
    wagerPoints: request.wagerPoints,
    pointValueInCents: 25,
    reelStops: [0, 1, 2, 3, 4],
    reels,
    consecutiveFiveMisses: 0,
    fiveMatchPityTriggered: false,
    isFreeSpin,
    freeSpinsAwarded: isBonus ? freeSpinsAwarded : 0,
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
    sealsAwarded: isCollection ? { SEAL_SYNC: 1 } : {},
    sealCollections: [
      { sealId: 'sync', count: 14, averageWagerPoints: request.wagerPoints, requiredCount: isRainbowRealm ? 60 : 28 },
      { sealId: 'rows', count: 8, averageWagerPoints: request.wagerPoints, requiredCount: isRainbowRealm ? 60 : 28 },
      { sealId: 'paw', count: 21, averageWagerPoints: request.wagerPoints, requiredCount: isRainbowRealm ? 60 : 28 },
      { sealId: 'rand', count: 4, averageWagerPoints: request.wagerPoints, requiredCount: isRainbowRealm ? 60 : 28 },
    ],
    freeSpinFeatureMode: featureMode,
    specialBoostApplied: false,
    slotsCreditsBalance: null,
    payout: payoutPoints > 0
      ? {
          totalPoints: payoutPoints,
          paylines: [{
            paylineId: 1,
            amountPoints: payoutPoints,
            matches: [{
              multiplier: 4,
              amountPoints: payoutPoints,
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
