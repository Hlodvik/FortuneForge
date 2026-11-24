import { act } from 'react'
import { createRoot, type Root } from 'react-dom/client'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { useSlotAudio } from '@fortuneforge/slot-client-core'
import { getPharaohResultCues, getPharaohTombResultCues, PHARAOHS_ASCENT_SOUNDS } from '../src/sounds'
import { usePharaohMusic } from '../src/usePharaohMusic'

class FakeAudio {
  loop = false
  volume = 1
  currentTime = 0
  paused = true
  play = vi.fn(async () => { this.paused = false })
  pause = vi.fn(() => { this.paused = true })
  addEventListener = vi.fn()
  constructor(readonly src: string) {}
}
const instances: FakeAudio[] = []
let audio: ReturnType<typeof useSlotAudio>
let music: ReturnType<typeof usePharaohMusic>
let root: Root
let host: HTMLDivElement
function Harness({ account = 'player-one' }: { account?: string }) {
  audio = useSlotAudio(PHARAOHS_ASCENT_SOUNDS)
  music = usePharaohMusic(account, audio)
  return null
}
const render = async (account?: string) => act(async () => root.render(<Harness account={account} />))
const confirm = async (id: string) => act(async () => music.confirmSpin(id))

beforeEach(() => {
  vi.stubGlobal('IS_REACT_ACT_ENVIRONMENT', true)
  vi.stubGlobal('AudioContext', undefined)
  vi.stubGlobal('Audio', class extends FakeAudio {
    constructor(source: string) { super(source); instances.push(this) }
  })
  localStorage.clear()
  instances.length = 0
  host = document.createElement('div')
  document.body.append(host)
  root = createRoot(host)
})
afterEach(async () => {
  await act(async () => root.unmount())
  expect(instances.every(instance => instance.paused)).toBe(true)
  host.remove()
  localStorage.clear()
  vi.unstubAllGlobals()
})

describe('Pharaoh recordings and arrangement progression', () => {
  it('plays the supplied riq, rattle, bell and transposed oud for their intended cues', async () => {
    await render()
    await act(async () => {
      audio.playCue('lever-pull'); audio.playCue('instruction-button')
      audio.startLoop('reel-spin'); audio.startLoop('special-reel-spin')
      audio.playSequence(getPharaohResultCues(95, 12, 0))
      audio.playSequence(getPharaohResultCues(96, 12, 0))
    })
    expect(instances.map(instance => instance.src.split('/').at(-1))).toEqual([
      'pharaoh-riq-hit.ogg', 'pharaoh-riq-hit.ogg', 'pharaoh-snake-rattle.ogg',
      'pharaoh-snake-rattle.ogg', 'pharaoh-bell-win.ogg', 'pharaoh-oud-big-win-g.ogg',
    ])
    expect(getPharaohResultCues(96, 12, 5)).toEqual(['big-win'])
    expect(getPharaohResultCues(0, 12, 0)).toEqual(['soft-miss'])
    expect(getPharaohResultCues(2, 12, 0)).toEqual(['low-win'])
    expect(getPharaohTombResultCues(95, 12)).toEqual(['bonus-complete'])
    expect(getPharaohTombResultCues(96, 12)).toEqual(['big-win'])
    await act(async () => { audio.stopLoop('reel-spin'); audio.stopLoop('special-reel-spin'); audio.stopResultCues() })
    expect(instances.slice(2).every(instance => instance.paused)).toBe(true)
  })

  it('switches only after four distinct confirmed spins, at the same position, and keeps it across reconnects', async () => {
    await render()
    expect(instances).toHaveLength(0)
    await act(async () => music.begin())
    const base = instances[0]
    base.currentTime = 37.5
    for (const id of ['one', 'one', 'two', 'three']) await confirm(id)
    expect(instances).toHaveLength(1)
    await confirm('four')
    const percussion = instances[1]
    expect(base.paused).toBe(true)
    expect(percussion.src).toContain('pharaoh-reworked-percussion.ogg')
    expect(percussion.currentTime).toBe(37.5)
    expect(percussion.loop).toBe(true)
    percussion.currentTime = 43
    await act(async () => { music.begin(); music.confirmSpin('five') })
    await render()
    expect(instances).toHaveLength(2)
    expect(percussion.currentTime).toBe(43)
    await render('player-two')
    expect(percussion.paused).toBe(true)
    await act(async () => music.begin())
    expect(instances[2].src).toContain('pharaoh-reworked.ogg')
  })

  it('respects mute, results-only and volume, then resumes the earned arrangement', async () => {
    await render()
    await act(async () => audio.toggleMuted())
    await act(async () => music.begin())
    for (const id of ['one', 'two', 'three', 'four']) await confirm(id)
    expect(instances).toHaveLength(0)
    await act(async () => audio.toggleMuted())
    expect(instances).toHaveLength(1)
    expect(instances[0].src).toContain('pharaoh-reworked-percussion.ogg')
    await act(async () => audio.setVolume(20))
    expect(instances[0].volume).toBeCloseTo(.35 * .2)
    await act(async () => audio.toggleResultsOnly())
    expect(instances[0].paused).toBe(true)
    await act(async () => audio.toggleResultsOnly())
    expect(instances[1].src).toContain('pharaoh-reworked-percussion.ogg')
  })
})

function parameter() {
  return { value: 1, setValueAtTime: vi.fn(), linearRampToValueAtTime: vi.fn(), cancelScheduledValues: vi.fn() }
}
function node() {
  return { connect: vi.fn(function (this: unknown) { return this }), disconnect: vi.fn() }
}
function context() {
  return { currentTime: 0, state: 'running', destination: {}, baseLatency: 0,
    resume: vi.fn(async () => {}), close: vi.fn(async () => {}),
    decodeAudioData: vi.fn(async () => ({ duration: 111.35528344671202 })),
    createBufferSource: vi.fn(() => ({ ...node(), buffer: null, loop: false, loopEnd: 0,
      start: vi.fn(), stop: vi.fn(), onended: null as (() => void) | null })),
    createGain: vi.fn(() => ({ ...node(), gain: parameter() })),
  }
}

describe('phase-aligned arrangement handoff', () => {
  it('crossfades on the original Web Audio clock, loops gaplessly and stops both sources on mute', async () => {
    const clock = context()
    vi.stubGlobal('AudioContext', class { constructor() { return clock } })
    vi.stubGlobal('fetch', vi.fn(async () => ({ ok: true, arrayBuffer: async () => new ArrayBuffer(0) })))
    await render()
    await act(async () => music.begin())
    const base = clock.createBufferSource.mock.results[0].value
    expect(base.loop).toBe(true)
    expect(base.start).toHaveBeenCalledWith(0)
    clock.currentTime = 27.125
    for (const id of ['one', 'two', 'three']) await confirm(id)
    expect(clock.createBufferSource).toHaveBeenCalledTimes(1)
    await confirm('four')
    const percussion = clock.createBufferSource.mock.results[1].value
    const [startAt, offset] = percussion.start.mock.calls[0] as unknown as [number, number]
    expect(startAt).toBeCloseTo(27.14)
    expect(offset).toBeCloseTo(27.14)
    expect(base.stop).toHaveBeenCalledWith(startAt + .35)
    expect(clock.createGain.mock.results[1].value.gain.linearRampToValueAtTime).toHaveBeenCalledWith(0, startAt + .35)
    expect(instances).toHaveLength(0)
    await act(async () => audio.toggleMuted())
    expect(base.stop).toHaveBeenCalledTimes(2)
    expect(percussion.stop).toHaveBeenCalledTimes(1)
  })

  it('waits for delayed decoding and cancels a pending handoff on account change', async () => {
    const clock = context()
    let finishDecode!: (value: { duration: number }) => void
    clock.decodeAudioData.mockImplementationOnce(() => new Promise(resolve => { finishDecode = resolve }))
    vi.stubGlobal('AudioContext', class { constructor() { return clock } })
    vi.stubGlobal('fetch', vi.fn(async () => ({ ok: true, arrayBuffer: async () => new ArrayBuffer(0) })))
    await render()
    await act(async () => music.begin())
    for (const id of ['one', 'two', 'three', 'four']) await confirm(id)
    expect(clock.createBufferSource).not.toHaveBeenCalled()
    await render('player-two')
    await act(async () => finishDecode({ duration: 111.35528344671202 }))
    expect(clock.createBufferSource).not.toHaveBeenCalled()
  })
})
