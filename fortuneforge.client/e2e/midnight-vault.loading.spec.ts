import { expect, test, type Page } from '@playwright/test'
import { writeFile } from 'node:fs/promises'

const assetPrefix = '/assets/slots/midnight-vault/'
const devices = [
  { name: 'phone-1x', viewport: { width: 390, height: 844 }, deviceScaleFactor: 1 },
  { name: 'phone-3x', viewport: { width: 390, height: 844 }, deviceScaleFactor: 3 },
  { name: 'desktop-1x', viewport: { width: 1366, height: 900 }, deviceScaleFactor: 1 },
  { name: 'desktop-2x', viewport: { width: 1366, height: 900 }, deviceScaleFactor: 2 },
] as const

// Dedicated loopback server replays real, seeded local engine responses and
// never proxies API requests. Avoid route interception so HTTP caching is real.
function requireLocalReview() {
  expect(process.env.MIDNIGHT_LOCAL_API).toBe('1')
}

async function waitForReelImages(page: Page) {
  await page.evaluate(() => new Promise<void>(resolve => requestAnimationFrame(() => requestAnimationFrame(() => resolve()))))
  await page.waitForFunction(() => {
    const images = [...document.querySelectorAll<HTMLImageElement>('.slot-symbol__image')]
    return images.length > 0 && images.every(image => {
      if (!image.currentSrc || !image.complete || !image.naturalWidth) return false
      const size = Number.parseFloat(image.sizes)
      const rendered = Math.min(image.clientWidth, image.clientHeight)
      const selected = Number(/symbols\/(\d+)\//.exec(image.currentSrc)?.[1])
      return Math.abs(size - rendered) <= 1 && selected >= Math.min(320, size * devicePixelRatio)
    })
  }, undefined, { timeout: 15_000 }).catch(async error => {
    console.log('Unready reel images', await page.locator('.slot-symbol__image').evaluateAll(images => images.map(node => {
      const image = node as HTMLImageElement
      return { src: image.getAttribute('src'), current: image.currentSrc, complete: image.complete,
        naturalWidth: image.naturalWidth, width: image.clientWidth, height: image.clientHeight }
    }).filter(image => !image.current || !image.complete || !image.naturalWidth)))
    throw error
  })
}

type ImageTransfer = {
  requestId: string; url: string; path: string; status: number; httpStatus?: number
  fromDiskCache: boolean; fromMemoryCache: boolean; fromServiceWorker: boolean
  wireBytes: number; bodyBytes: number; decodedBodyBytes: number
  contentLength: number | null; failed?: boolean
}

function measuredTransfers(transfers: readonly ImageTransfer[]) {
  return transfers.map(item => ({ ...item, headerBytes: Math.max(0, item.wireBytes - item.bodyBytes) }))
}

async function selectedImages(page: Page) {
  return page.locator('.slot-symbol__image').evaluateAll(images => images.map(node => {
    const image = node as HTMLImageElement
    return { src: new URL(image.currentSrc || image.src, location.href).pathname, sizes: Number.parseFloat(image.sizes),
      cssWidth: Math.min(image.clientWidth, image.clientHeight), dpr: devicePixelRatio }
  }))
}

for (const device of devices) {
  test(`Midnight Vault cold requests and resize: ${device.name}`, async ({ browser, baseURL }, testInfo) => {
    test.setTimeout(90_000)
    const context = await browser.newContext({ ...device, baseURL, serviceWorkers: 'block', reducedMotion: 'reduce' })
    try {
      const page = await context.newPage()
      const requests: ImageTransfer[] = []
      const responses = new Map<string, ImageTransfer>()
      const cached = new Set<string>()
      const network = await context.newCDPSession(page)
      await network.send('Network.enable')
      const realisticCache = process.env.MIDNIGHT_LOCAL_API === '1'
      await network.send('Network.setCacheDisabled', { cacheDisabled: !realisticCache })
      network.on('Network.requestServedFromCache', event => cached.add(event.requestId))
      network.on('Network.responseReceived', event => {
        if (event.type === 'Image' && event.response.url.startsWith('http')) {
          const length = Object.entries(event.response.headers).find(([name]) => name.toLowerCase() === 'content-length')?.[1]
          responses.set(event.requestId, { requestId: event.requestId, url: event.response.url,
            path: new URL(event.response.url).pathname, status: event.response.status,
            fromDiskCache: event.response.fromDiskCache ?? false,
            fromMemoryCache: cached.has(event.requestId), fromServiceWorker: event.response.fromServiceWorker ?? false,
            contentLength: length === undefined ? null : Number(length),
            wireBytes: 0, bodyBytes: 0, decodedBodyBytes: 0 })
        }
      })
      network.on('Network.responseReceivedExtraInfo', event => {
        const response = responses.get(event.requestId)
        if (response) response.httpStatus = event.statusCode
      })
      network.on('Network.dataReceived', event => {
        const response = responses.get(event.requestId)
        if (response) { response.bodyBytes += event.encodedDataLength; response.decodedBodyBytes += event.dataLength }
      })
      network.on('Network.loadingFinished', event => {
        const response = responses.get(event.requestId)
        if (response) { response.wireBytes = event.encodedDataLength; requests.push(response) }
        responses.delete(event.requestId)
      })
      network.on('Network.loadingFailed', event => {
        const response = responses.get(event.requestId)
        if (response) requests.push({ ...response, failed: true })
        responses.delete(event.requestId)
      })
      requireLocalReview()
      await page.goto('/slots/dragon-hoard/demo')
      await expect(page.getByRole('button', { name: 'Spin the reels' })).toBeEnabled()
      await expect(page.getByRole('heading', { name: 'Midnight Vault', exact: true })).toHaveCount(1)
      await test.step('load measured reel images', () => waitForReelImages(page))
      const cold = await selectedImages(page)
      for (const image of cold) {
        const needed = image.sizes * image.dpr
        const expected = [96, 192, 320].find(width => width >= needed) ?? 320
        expect(image.src).toContain(`${assetPrefix}v4/symbols/${expected}/`)
        expect(Math.abs(image.sizes - image.cssWidth)).toBeLessThanOrEqual(1)
      }
      await expect.poll(() => requests.filter(request => request.path.includes(`${assetPrefix}v1/presentation/midnight-vault-backdrop-`)).length).toBe(1)
      const coldRequests = measuredTransfers(requests)
      console.log(device.name, 'cold image bytes', coldRequests.reduce((sum, request) => sum + request.bodyBytes, 0))
      expect(coldRequests.some(request => request.path.includes('/games/dragon-hoard/'))).toBe(false)
      const permitted = new Set(cold.map(image => image.src))
      // Collection gems can legitimately select a smaller variant than reels.
      for (const request of coldRequests.filter(request => request.path.includes(`${assetPrefix}v4/symbols/`))) {
        expect(permitted.has(request.path) || /-(?:ruby|sapphire|amber|emerald)-access\.webp$/.test(request.path)).toBe(true)
        // The cabinet can use larger symbols as its composition changes;
        // choose by the measured CSS footprint rather than a fixed device tier.
      }
      expect(coldRequests.filter(request => request.path.includes(`${assetPrefix}v1/presentation/midnight-vault-backdrop-`))).toHaveLength(1)
      const downloaded = coldRequests.filter(request => request.bodyBytes > 0)
      expect(downloaded.some(request => request.path.includes(assetPrefix))).toBe(true)
      if (realisticCache) {
        for (const url of new Set(downloaded.map(request => request.url))) {
          expect(downloaded.filter(request => request.url === url)).toHaveLength(1)
        }
      }
      expect(new Set(downloaded.filter(request => request.path.includes('midnight-vault-catalogue-title')).map(request => request.url)).size).toBe(1)
      expect(await page.locator('link[rel="preload"][as="image"]').count()).toBe(0)
      await writeFile(testInfo.outputPath(`${device.name}-cold-network.json`), JSON.stringify({ device, cold, coldRequests }, null, 2))
      await page.screenshot({ path: testInfo.outputPath(`${device.name}-idle.jpg`), type: 'jpeg', quality: 80 })

      let repeatRequests: ReturnType<typeof measuredTransfers> = []
      if (realisticCache) {
        const start = requests.length
        await page.reload()
        await waitForReelImages(page)
        await expect.poll(() => requests.slice(start).some(request => request.path.includes('midnight-vault-backdrop-'))).toBe(true)
        repeatRequests = measuredTransfers(requests.slice(start))
        expect(repeatRequests.reduce((sum, request) => sum + request.bodyBytes, 0)).toBe(0)
      }

      await page.setViewportSize(device.viewport.width < 721 ? { width: 1440, height: 1000 } : { width: 390, height: 844 })
      await waitForReelImages(page)
      const resized = await selectedImages(page)
      for (const image of resized) {
        const selected = Number(/symbols\/(\d+)\//.exec(image.src)?.[1])
        // Browsers may keep a sharper cached resource when the viewport shrinks.
        expect(selected).toBeGreaterThanOrEqual(Math.min(320, image.sizes * image.dpr))
      }
      await page.getByRole('button', { name: 'Spin the reels' }).click()
      await expect(page.getByRole('button', { name: 'Spin the reels' })).toBeEnabled()
      await page.getByRole('button', { name: 'Spin the reels' }).click()
      await expect(page.locator('.slots-page--special-game')).toBeVisible()
      await expect(page.locator('.slot-symbol')).toHaveCount(30)
      await expect(page.getByRole('button', { name: 'Spin the reels' })).toBeEnabled()
      await waitForReelImages(page)
      await page.screenshot({ path: testInfo.outputPath(`${device.name}-expanded-after-resize.jpg`), type: 'jpeg', quality: 80 })
      const clipped = await page.locator('.slot-symbol__image').evaluateAll(images => {
        const frame = document.querySelector('.slot-machine__reels')!.getBoundingClientRect()
        return images.filter(image => {
          const box = image.getBoundingClientRect()
          return box.left < frame.left - 1 || box.right > frame.right + 1 || box.top < frame.top - 1 || box.bottom > frame.bottom + 1
        }).length
      })
      expect(clipped).toBe(0)
      const artwork = coldRequests.filter(request => request.path.includes(assetPrefix) || request.path.includes('midnight-vault-catalogue-title'))
      await writeFile(testInfo.outputPath(`${device.name}-network.json`), JSON.stringify({
        device, cold, coldRequests,
        coldImageBodyBytes: coldRequests.reduce((sum, request) => sum + request.bodyBytes, 0),
        coldImageWireBytes: coldRequests.reduce((sum, request) => sum + request.wireBytes, 0),
        coldMidnightArtworkBodyBytes: artwork.reduce((sum, request) => sum + request.bodyBytes, 0),
        cacheMode: realisticCache ? 'normal-http-cache' : 'disabled-diagnostic', repeatRequests,
        repeatImageBodyBytes: repeatRequests.reduce((sum, request) => sum + request.bodyBytes, 0),
        resized, expanded: await selectedImages(page), afterResizeAndMockBonusRequests: measuredTransfers(requests),
      }, null, 2))
    } finally {
      await context.close()
    }
  })
}
