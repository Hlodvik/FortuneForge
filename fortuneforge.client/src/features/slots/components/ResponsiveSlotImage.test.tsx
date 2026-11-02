// @vitest-environment jsdom
import { act } from 'react'
import { createRoot, type Root } from 'react-dom/client'
import { renderToStaticMarkup } from 'react-dom/server'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { getSlotSymbolImageSrcSet, type SlotSymbolSet } from '../config/symbolSets'
import { ResponsiveSlotImage } from './ResponsiveSlotImage'
import { SlotSymbol } from './SlotSymbol'

const candidates = '/art-96.webp 96w, /art-192.webp 192w, /art-320.webp 320w'
let host: HTMLDivElement
let root: Root
let cssWidth: number
let cssHeight: number
let notifyResize: () => void
let objectFit: string
const disconnect = vi.fn()
const observe = vi.fn()
const observerCreated = vi.fn()

beforeEach(() => {
  vi.stubGlobal('IS_REACT_ACT_ENVIRONMENT', true)
  cssWidth = 0
  cssHeight = 0
  objectFit = 'contain'
  notifyResize = () => undefined
  vi.spyOn(HTMLImageElement.prototype, 'clientWidth', 'get').mockImplementation(() => cssWidth)
  vi.spyOn(HTMLImageElement.prototype, 'clientHeight', 'get').mockImplementation(() => cssHeight)
  vi.stubGlobal('getComputedStyle', () => ({ objectFit }))
  vi.stubGlobal('ResizeObserver', class {
    constructor(callback: () => void) { observerCreated(); notifyResize = callback }
    observe = observe
    disconnect = disconnect
  })
  host = document.createElement('div')
  document.body.append(host)
  root = createRoot(host)
})

afterEach(() => {
  act(() => root.unmount())
  host.remove()
  vi.restoreAllMocks()
  vi.unstubAllGlobals()
  vi.clearAllMocks()
})

describe('ResponsiveSlotImage', () => {
  it('keeps legacy src markup immediate and creates no measurement observer', () => {
    act(() => root.render(<ResponsiveSlotImage src="/legacy.gif" className="legacy" alt="" />))
    const image = host.querySelector('img')!
    expect(image.getAttribute('src')).toBe('/legacy.gif')
    expect(image.hasAttribute('srcset')).toBe(false)
    expect(image.hasAttribute('sizes')).toBe(false)
    expect(image.className).toBe('legacy')
    expect(observerCreated).not.toHaveBeenCalled()
  })

  it('withholds every source until a nonzero CSS size is known', () => {
    act(() => root.render(<ResponsiveSlotImage src="/art-320.webp" imageSrcSet={candidates} alt="" />))
    const image = host.querySelector('img')!
    expect(image.hasAttribute('src')).toBe(false)
    expect(image.hasAttribute('srcset')).toBe(false)
    expect(image.hasAttribute('sizes')).toBe(false)
    expect(observe).toHaveBeenCalledWith(image)
    cssWidth = 46
    cssHeight = 46
    act(() => notifyResize())
    expect(image.getAttribute('sizes')).toBe('46px')
    expect(image.getAttribute('srcset')).toBe(candidates)
    expect(image.getAttribute('src')).toBe('/art-320.webp')
    expect(host.querySelector('link[rel="preload"]')).toBeNull()
  })

  it('uses actual contained square width and updates sizes on container resize', () => {
    cssWidth = 116
    cssHeight = 87
    act(() => root.render(<ResponsiveSlotImage src="/art-320.webp" imageSrcSet={candidates} alt="" />))
    const image = host.querySelector('img')!
    expect(image.getAttribute('sizes')).toBe('87px')
    cssWidth = 61
    cssHeight = 46
    act(() => notifyResize())
    expect(image.getAttribute('sizes')).toBe('46px')
    expect(image.getAttribute('srcset')).toBe(candidates)
  })

  it('uses title width and height attributes to preserve a2:1 bitmap measurement', () => {
    cssWidth = 320
    cssHeight = 160
    act(() => root.render(<ResponsiveSlotImage src="/title.webp" imageSrcSet="/title.webp 768w" width={768} height={384} alt="" />))
    expect(host.querySelector('img')!.getAttribute('sizes')).toBe('320px')
  })

  it('uses CSS width for cover artwork and remeasures without ResizeObserver', () => {
    vi.stubGlobal('ResizeObserver', undefined)
    objectFit = 'cover'
    cssWidth = 220
    cssHeight = 100
    act(() => root.render(<ResponsiveSlotImage src="/art-320.webp" imageSrcSet={candidates} alt="" />))
    expect(host.querySelector('img')!.getAttribute('sizes')).toBe('220px')
    cssWidth = 175
    act(() => window.dispatchEvent(new Event('resize')))
    expect(host.querySelector('img')!.getAttribute('sizes')).toBe('175px')
    expect(observerCreated).not.toHaveBeenCalled()
  })

  it('withholds responsive sources in server markup and leaves legacy markup unchanged', () => {
    const responsive = renderToStaticMarkup(<ResponsiveSlotImage src="/art-320.webp" imageSrcSet={candidates} alt="" />)
    expect(responsive).not.toContain('src=')
    expect(responsive).not.toContain('srcSet=')
    expect(responsive).not.toContain('rel="preload"')
    expect(renderToStaticMarkup(<ResponsiveSlotImage src="/legacy.webp" alt="" />)).toContain('src="/legacy.webp"')
  })

  it('disconnects the observer on unmount', () => {
    act(() => root.render(<ResponsiveSlotImage src="/art-320.webp" imageSrcSet={candidates} alt="" />))
    act(() => root.render(<div />))
    expect(disconnect).toHaveBeenCalledOnce()
  })
})

describe('SlotSymbol responsive animation compatibility', () => {
  const symbolSet: SlotSymbolSet = {
    id: 'fixture', definitions: { ACE: {
      id: 'ACE', label: 'Wild', image: '/art-320.webp',
      responsiveImages: { 96: '/art-96.webp', 192: '/art-192.webp', 320: '/art-320.webp' },
      animatedImage: '/legacy-animation.gif',
    } }, guideEntries: [],
  }

  it('keeps a distinct animated source immediate without static srcset candidates', () => {
    act(() => root.render(<SlotSymbol symbol="ACE" symbolSet={symbolSet} />))
    expect(host.querySelector('img')!.getAttribute('src')).toBe('/legacy-animation.gif')
    expect(host.querySelector('img')!.hasAttribute('srcset')).toBe(false)
    expect(host.querySelector('.slot-symbol__artwork--responsive')).toBeNull()
    expect(observerCreated).not.toHaveBeenCalled()
  })

  it('uses responsive static candidates when animation is disabled', () => {
    cssWidth = 80
    cssHeight = 60
    act(() => root.render(<SlotSymbol symbol="ACE" symbolSet={symbolSet} animated={false} />))
    expect(host.querySelector('img')!.getAttribute('src')).toBe('/art-320.webp')
    expect(host.querySelector('img')!.getAttribute('srcset')).toBe(candidates)
    expect(host.querySelector('img')!.getAttribute('sizes')).toBe('60px')
    expect(host.querySelector('.slot-symbol__artwork--responsive')).not.toBeNull()
  })

  it('retains256px animated master candidates without inventing an upscaled320px source', () => {
    expect(getSlotSymbolImageSrcSet({ 96: '/fruit-96.webp', 192: '/fruit-192.webp', 256: '/fruit-256.webp' }))
      .toBe('/fruit-96.webp 96w, /fruit-192.webp 192w, /fruit-256.webp 256w')
    expect(getSlotSymbolImageSrcSet()).toBeUndefined()
  })
})
