import { expect, test, type Locator, type Page } from '@playwright/test'
import { stat, writeFile } from 'node:fs/promises'
import { resolve } from 'node:path'

const reviewRoute = '/slots/dragon-hoard/demo?review=base-loss'
const musicPath = '/assets/slots/midnight-vault/v3/audio/i-knew-a-guy-kevin-macleod.mp3'
const viewports = [
  { name: 'laptop-874x764', width: 874, height: 764 },
  { name: 'desktop-1280x720', width: 1280, height: 720 },
  { name: 'desktop-1366x900', width: 1366, height: 900 },
  { name: 'compact-900x600', width: 900, height: 600 },
  { name: 'phone-390x844', width: 390, height: 844 },
] as const

async function ready(page: Page) {
  await expect(page.getByRole('button', { name: 'Spin the reels', exact: true })).toBeEnabled()
  await page.waitForFunction(() => {
    const images = [...document.querySelectorAll<HTMLImageElement>('.slot-symbol__image')]
    return images.length === 20 && images.every(image => image.complete && image.naturalWidth > 0)
  })
  await page.evaluate(async () => {
    await document.fonts.ready
    await new Promise<void>(done => requestAnimationFrame(() => requestAnimationFrame(() => done())))
  })
}

async function layoutState(page: Page) {
  return page.evaluate(() => {
    const rect = (selector: string) => {
      const bounds = document.querySelector(selector)!.getBoundingClientRect()
      return { x: bounds.x, y: bounds.y, width: bounds.width, height: bounds.height,
        right: bounds.right, bottom: bounds.bottom }
    }
    const font = (selector: string) => getComputedStyle(document.querySelector(selector)!).fontFamily
    const reels = rect('.slot-machine__reels')
    return {
      viewport: { width: innerWidth, height: innerHeight },
      scroll: { width: document.documentElement.scrollWidth, height: document.documentElement.scrollHeight },
      stage: rect('.slots-page__stage'), frame: rect('.slot-game-frame'), reels,
      console: rect('.slots-page__playbar'), spin: rect('.spin-button--vault-dial'),
      navbar: rect('.in-game-navbar--slots'),
      fonts: { wager: font('.slots-page__wager-display strong'), balance: font('.slots-page__balance-value'),
        loaded: document.fonts.check('500 14px "Sora Variable"') },
      images: [...document.querySelectorAll<HTMLImageElement>('.slot-symbol__image')].map(image => {
        const bounds = image.getBoundingClientRect()
        return { src: new URL(image.currentSrc).pathname, width: bounds.width, height: bounds.height,
          clipped: bounds.left < reels.x - 1 || bounds.top < reels.y - 1 || bounds.right > reels.right + 1 || bounds.bottom > reels.bottom + 1 }
      }),
    }
  })
}

async function dialogState(dialog: Locator) {
  return dialog.evaluate(panel => {
    const style = getComputedStyle(panel)
    const close = panel.querySelector<HTMLButtonElement>('button[aria-label^="Close"]')!
    const icon = close.querySelector('svg')!
    const panelBounds = panel.getBoundingClientRect()
    const buttonBounds = close.getBoundingClientRect()
    const iconBounds = icon.getBoundingClientRect()
    const colors = [...style.backgroundImage.matchAll(/rgba?\((\d+),\s*(\d+),\s*(\d+)/g)]
      .map(match => ({ r: Number(match[1]), g: Number(match[2]), b: Number(match[3]) }))
    return { theme: panel.getAttribute('data-slot-dialog-theme'), font: style.fontFamily,
      background: style.backgroundImage, colors,
      bounds: { x: panelBounds.x, y: panelBounds.y, right: panelBounds.right, bottom: panelBounds.bottom },
      close: { accessibleName: close.getAttribute('aria-label'), text: close.textContent,
        offsetX: Math.abs(buttonBounds.x + buttonBounds.width / 2 - iconBounds.x - iconBounds.width / 2),
        offsetY: Math.abs(buttonBounds.y + buttonBounds.height / 2 - iconBounds.y - iconBounds.height / 2) },
    }
  })
}

test.beforeEach(async ({ baseURL }) => {
  expect(process.env.MIDNIGHT_LOCAL_API).toBe('1')
  expect(new URL(baseURL!).hostname).toBe('127.0.0.1')
})

for (const viewport of viewports) {
  test(`Midnight Vault V3 composition: ${viewport.name}`, async ({ page }, testInfo) => {
    await page.setViewportSize({ width: viewport.width, height: viewport.height })
    await page.goto(reviewRoute)
    await ready(page)
    const state = await layoutState(page)
    await page.screenshot({ path: testInfo.outputPath(`${viewport.name}.png`) })
    await writeFile(testInfo.outputPath(`${viewport.name}.json`), JSON.stringify(state, null, 2))

    await expect(page.locator('.in-game-navbar--slots')).toHaveCount(1)
    await expect(page.locator('.in-game-navbar--slots').getByRole('link', { name: 'Fortune Forge home' })).toBeVisible()
    await expect(page.locator('.in-game-navbar--slots').getByRole('link', { name: 'Other Games' })).toBeVisible()
    await expect(page.locator('.slots-page__topbar')).toHaveCount(0)
    await expect(page.locator('.midnight-vault__wordmark img')).toBeVisible()
    await expect(page.locator('.midnight-vault__masthead')).not.toContainText(/Unlock the Midnight Vault|Fortune Forge presents/i)
    expect(state.scroll.width).toBeLessThanOrEqual(viewport.width)
    expect(state.scroll.height).toBeLessThanOrEqual(viewport.height + 1)
    expect(state.stage.y).toBeGreaterThanOrEqual(state.navbar.bottom)
    expect(state.console.bottom).toBeLessThanOrEqual(viewport.height)
    expect(state.spin.bottom).toBeLessThanOrEqual(viewport.height)
    expect(state.images).toHaveLength(20)
    expect(state.images.filter(image => image.clipped || image.width <= 0 || image.height <= 0)).toEqual([])
    expect(state.fonts.wager).toContain('Sora Variable')
    expect(state.fonts.balance).toContain('Sora Variable')
    expect(state.fonts.loaded).toBe(true)
    if (viewport.width > 720) {
      expect(state.stage.width).toBeLessThanOrEqual(viewport.width * 0.85)
      expect(state.stage.x).toBeGreaterThanOrEqual(viewport.width * 0.07)
      expect(viewport.width - state.stage.right).toBeGreaterThanOrEqual(viewport.width * 0.07)
    } else {
      expect(state.stage.x).toBeGreaterThanOrEqual(6)
      expect(state.stage.right).toBeLessThanOrEqual(viewport.width - 6)
    }

    const tracks = page.locator('[data-collection-presentation="vault-access"]')
    await expect(tracks).toHaveCount(4)
    await tracks.first().hover()
    await expect(tracks.locator('[role="tooltip"]')).toHaveCount(0)
    await expect(page.locator('.slots-page__collection-tooltip')).toHaveCount(0)
    expect(await tracks.first().getAttribute('aria-describedby')).toBeNull()

    const info = page.getByRole('button', { name: 'Symbol values', exact: true })
    const trigger = await info.evaluate(button => {
      const style = getComputedStyle(button)
      const bounds = button.getBoundingClientRect()
      return { border: ['borderTopWidth', 'borderRightWidth', 'borderBottomWidth', 'borderLeftWidth']
        .map(key => Number.parseFloat(style[key as keyof CSSStyleDeclaration] as string)),
      background: style.backgroundColor, shadow: style.boxShadow,
      width: bounds.width, height: bounds.height, text: button.textContent }
    })
    expect(trigger.border).toEqual([0, 0, 0, 0])
    expect(trigger.background).toBe('rgba(0, 0, 0, 0)')
    expect(trigger.shadow).toBe('none')
    expect(trigger.width).toBeGreaterThanOrEqual(44)
    expect(trigger.height).toBeGreaterThanOrEqual(44)
    expect(trigger.text).toBe('')
    await expect(info.locator('svg circle[cx="12"][cy="12"][r="9"]')).toHaveCount(1)
  })
}

for (const viewport of [{ name: 'laptop', width: 874, height: 764 }, { name: 'phone', width: 390, height: 844 }]) {
  test(`Midnight Vault V3 themed dialogs: ${viewport.name}`, async ({ page }, testInfo) => {
    await page.setViewportSize({ width: viewport.width, height: viewport.height })
    await page.goto(reviewRoute)
    await ready(page)
    const records = []
    for (const [name, triggerName, dialogName] of [
      ['values', 'Symbol values', 'Symbol values'],
      ['rules', 'How to win', 'What counts as a win?'],
      ['settings', 'Open settings', 'Settings'],
    ]) {
      const trigger = page.getByRole('button', { name: triggerName, exact: true })
      await trigger.click()
      const dialog = page.getByRole('dialog', { name: dialogName, exact: true })
      await expect(dialog).toBeVisible()
      const state = await dialogState(dialog)
      records.push({ name, ...state })
      await page.screenshot({ path: testInfo.outputPath(`${viewport.name}-${name}-dialog.png`) })
      expect(state.theme).toBe('midnight-vault')
      expect(state.font).toContain('Sora Variable')
      expect(state.colors.length).toBeGreaterThan(0)
      expect(state.colors.every(color => color.b > color.g && color.g >= color.r)).toBe(true)
      expect(state.close.text).not.toContain('×')
      expect(state.close.offsetX).toBeLessThanOrEqual(1)
      expect(state.close.offsetY).toBeLessThanOrEqual(1)
      expect(state.bounds.x).toBeGreaterThanOrEqual(8)
      expect(state.bounds.y).toBeGreaterThanOrEqual(8)
      expect(state.bounds.right).toBeLessThanOrEqual(viewport.width - 8)
      expect(state.bounds.bottom).toBeLessThanOrEqual(viewport.height - 8)
      await page.keyboard.press('Escape')
      await expect(dialog).toHaveCount(0)
      await expect(trigger).toBeFocused()
    }
    await writeFile(testInfo.outputPath(`${viewport.name}-dialogs.json`), JSON.stringify(records, null, 2))
  })
}

test('Midnight Vault V3 licensed music waits for a gesture and obeys volume and mute', async ({ page }, testInfo) => {
  test.setTimeout(60_000)
  const requested: string[] = []
  page.on('request', request => { if (/\.mp3(?:\?|$)/.test(request.url())) requested.push(request.url()) })
  await page.addInitScript(() => {
    const state = window as typeof window & { __midnightReviewAudio: HTMLMediaElement[] }
    state.__midnightReviewAudio = []
    const originalPlay = HTMLMediaElement.prototype.play
    HTMLMediaElement.prototype.play = function () {
      if (this.src.includes('/midnight-vault/v3/audio/')) state.__midnightReviewAudio.push(this)
      return originalPlay.call(this)
    }
  })
  const audioState = () => page.evaluate(() => {
    const state = window as typeof window & { __midnightReviewAudio: HTMLMediaElement[] }
    return [...new Set(state.__midnightReviewAudio)].map(audio => ({
      src: audio.currentSrc || audio.src, paused: audio.paused, volume: audio.volume,
      readyState: audio.readyState, currentTime: audio.currentTime,
    }))
  })
  await page.setViewportSize({ width: 874, height: 764 })
  await page.goto(reviewRoute)
  await ready(page)
  const beforeGesture = { requested: [...requested], audio: await audioState() }
  expect(beforeGesture.requested).toEqual([])
  expect(beforeGesture.audio).toEqual([])

  const responsePromise = page.waitForResponse(response => new URL(response.url()).pathname === musicPath)
  await page.getByRole('button', { name: 'Open settings', exact: true }).click()
  const response = await responsePromise
  expect(response.status()).toBe(200)
  expect(await response.finished()).toBeNull()
  const assetBytes = (await stat(resolve(`public${musicPath}`))).size
  expect(assetBytes).toBeGreaterThan(6_000_000)
  expect(Number(response.headers()['content-length'])).toBe(assetBytes)
  expect(response.headers()['content-type']).toContain('audio/mpeg')
  await expect.poll(async () => (await audioState()).some(audio => !audio.paused && audio.readyState >= 2 && audio.currentTime > 0)).toBe(true)
  const initial = await audioState()

  const mute = page.getByRole('button', { name: 'Mute Turn off all game audio.', exact: true })
  await mute.click()
  await expect(mute).toHaveAttribute('aria-pressed', 'true')
  await expect.poll(async () => (await audioState()).every(audio => audio.paused)).toBe(true)
  const slider = page.getByRole('slider', { name: 'Volume', exact: true })
  await slider.focus()
  await slider.press('Home')
  for (let value = 0; value < 25; value++) await slider.press('ArrowRight')
  await expect(slider).toHaveValue('25')
  await mute.click()
  await expect(mute).toHaveAttribute('aria-pressed', 'false')
  await expect.poll(async () => (await audioState()).some(audio => !audio.paused && Math.abs(audio.volume - 0.01) < 0.0001)).toBe(true)
  const adjusted = await audioState()
  await mute.click()
  await expect.poll(async () => (await audioState()).every(audio => audio.paused)).toBe(true)
  const preferences = await page.evaluate(() => JSON.parse(localStorage.getItem('fortune-forge.audio-preferences.v2')!))
  expect(preferences).toMatchObject({ volume: 25, mode: 'muted' })
  await page.screenshot({ path: testInfo.outputPath('licensed-music-settings.png') })
  await writeFile(testInfo.outputPath('licensed-music.json'), JSON.stringify({
    beforeGesture, requested, source: musicPath, status: response.status(), assetBytes,
    initial, adjusted, muted: await audioState(), preferences,
    verification: 'Actual browser request, decode/play state and controls; no audible quality claim.',
  }, null, 2))
})
