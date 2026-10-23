import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

const muteStorageKey = 'fortuneforge:craps:muted'

describe('Craps optional browser audio', () => {
  beforeEach(() => {
    vi.resetModules()
    FakeAudioContext.instances = []
    FakeAudioContext.autoResume = true
    FakeAudioContext.rejectResume = false
    const stored = new Map<string, string>()
    vi.stubGlobal('localStorage', {
      getItem: vi.fn((key: string) => stored.get(key) ?? null),
      setItem: vi.fn((key: string, value: string) => stored.set(key, value)),
    })
    vi.stubGlobal('window', { AudioContext: FakeAudioContext })
  })

  afterEach(async () => {
    const audio = await import('./crapsAudio')
    audio.disposeCrapsAudio()
    vi.unstubAllGlobals()
  })

  it('does not autoplay on import, result callbacks, or unmuting', async () => {
    const audio = await import('./crapsAudio')
    audio.playCrapsResult(true)
    audio.setCrapsMuted(false)
    expect(FakeAudioContext.instances).toHaveLength(0)
  })

  it('unlocks from clicks and reuses one context and one small noise buffer', async () => {
    const audio = await import('./crapsAudio')
    audio.playCrapsClick()
    await flushPromises()
    audio.playCrapsRoll()
    audio.playCrapsResult(true)
    const context = FakeAudioContext.instances[0]!

    expect(FakeAudioContext.instances).toHaveLength(1)
    expect(context.resume).toHaveBeenCalledOnce()
    expect(context.createBuffer).toHaveBeenCalledOnce()
    expect(context.createBuffer.mock.calls[0]![1]).toBe(context.sampleRate * .8)
    expect(context.oscillators.length).toBeGreaterThan(20)
  })

  it('builds an irregular dice tumble with a rolling bed and many separate contacts', async () => {
    const audio = await import('./crapsAudio')
    audio.playCrapsRoll()
    await flushPromises()
    const context = FakeAudioContext.instances[0]!
    const contacts = context.bufferSources.slice(1)
    const starts = contacts.map(source => source.start.mock.calls[0]![0] as number)
    const gaps = starts.slice(1).map((at, index) => at - starts[index]!)

    expect(context.bufferSources).toHaveLength(12)
    expect(context.oscillators).toHaveLength(22)
    expect(context.bufferSources[0]!.stop.mock.calls[0]![0]).toBeCloseTo(1.688)
    expect(starts.at(-1)).toBeGreaterThan(1.7)
    expect(Math.max(...gaps) - Math.min(...gaps)).toBeGreaterThan(.04)
    expect(contacts.every(source => source.buffer === context.bufferSources[0]!.buffer)).toBe(true)
  })

  it('stops an old roll before another roll so repeat clicks cannot build a sound wall', async () => {
    const audio = await import('./crapsAudio')
    audio.playCrapsRoll()
    await flushPromises()
    const context = FakeAudioContext.instances[0]!
    const oldSources = [...context.bufferSources, ...context.oscillators]

    audio.playCrapsRoll()

    expect(oldSources.every(source => source.stop.mock.calls.length === 2)).toBe(true)
    expect(oldSources.every(source => source.disconnect.mock.calls.length === 1)).toBe(true)
    expect(context.bufferSources).toHaveLength(24)
  })

  it('limits rapid button click overlap while preserving a single shared context', async () => {
    const audio = await import('./crapsAudio')
    audio.playCrapsClick()
    await flushPromises()
    for (let index = 0; index < 10; index++) audio.playCrapsClick()
    const context = FakeAudioContext.instances[0]!

    expect(FakeAudioContext.instances).toHaveLength(1)
    expect(context.bufferSources.filter(source => source.disconnect.mock.calls.length === 0)).toHaveLength(4)
  })

  it('persists scoped mute and stops every scheduled cue immediately', async () => {
    const audio = await import('./crapsAudio')
    audio.playCrapsRoll()
    await flushPromises()
    const context = FakeAudioContext.instances[0]!
    const sources = [...context.bufferSources, ...context.oscillators]

    audio.setCrapsMuted(true)
    audio.playCrapsClick()
    audio.playCrapsRoll()
    audio.playCrapsResult(true)

    expect(audio.getCrapsMuted()).toBe(true)
    expect(localStorage.setItem).toHaveBeenCalledWith(muteStorageKey, 'true')
    expect(sources.every(source => source.stop.mock.calls.length === 2)).toBe(true)
    expect(context.bufferSources).toHaveLength(12)
    audio.setCrapsMuted(false)
    expect(localStorage.setItem).toHaveBeenLastCalledWith(muteStorageKey, 'false')
    expect(context.bufferSources).toHaveLength(12)
  })

  it('honours saved mute without constructing any audio', async () => {
    localStorage.setItem(muteStorageKey, 'true')
    const audio = await import('./crapsAudio')
    audio.playCrapsClick()
    audio.playCrapsRoll()

    expect(audio.getCrapsMuted()).toBe(true)
    expect(FakeAudioContext.instances).toHaveLength(0)
  })

  it('retains in-memory mute when browser storage is denied', async () => {
    vi.stubGlobal('localStorage', {
      getItem: () => { throw new Error('denied') },
      setItem: () => { throw new Error('denied') },
    })
    const audio = await import('./crapsAudio')

    expect(() => audio.setCrapsMuted(true)).not.toThrow()
    audio.playCrapsClick()
    expect(audio.getCrapsMuted()).toBe(true)
    expect(FakeAudioContext.instances).toHaveLength(0)
    audio.setCrapsMuted(false)
    audio.playCrapsClick()
    await flushPromises()
    expect(FakeAudioContext.instances).toHaveLength(1)
  })

  it('coalesces gesture cues while suspended, playing only the latest one', async () => {
    FakeAudioContext.autoResume = false
    const audio = await import('./crapsAudio')
    audio.playCrapsClick()
    audio.playCrapsRoll()
    const context = FakeAudioContext.instances[0]!
    audio.playCrapsResult(true)
    expect(context.bufferSources).toHaveLength(0)

    context.finishResume()
    await flushPromises()

    expect(context.resume).toHaveBeenCalledOnce()
    expect(context.bufferSources).toHaveLength(12)
    expect(context.oscillators).toHaveLength(22)
  })

  it.each(['mute', 'dispose'] as const)('invalidates pending resume when %s happens before unlocking', async action => {
    FakeAudioContext.autoResume = false
    const audio = await import('./crapsAudio')
    audio.playCrapsRoll()
    const context = FakeAudioContext.instances[0]!
    if (action === 'mute') audio.setCrapsMuted(true)
    else audio.disposeCrapsAudio()
    context.finishResume()
    await flushPromises()

    expect(context.bufferSources).toHaveLength(0)
    expect(context.oscillators).toHaveLength(0)
  })

  it('swallows browser resume rejection and permits a later gesture to try again', async () => {
    FakeAudioContext.rejectResume = true
    const audio = await import('./crapsAudio')
    audio.playCrapsRoll()
    await flushPromises()
    const context = FakeAudioContext.instances[0]!
    expect(context.bufferSources).toHaveLength(0)

    FakeAudioContext.rejectResume = false
    audio.playCrapsClick()
    await flushPromises()

    expect(context.resume).toHaveBeenCalledTimes(2)
    expect(context.bufferSources).toHaveLength(1)
  })

  it('releases completed nodes and can reopen after disposal or a closed context', async () => {
    const audio = await import('./crapsAudio')
    audio.playCrapsClick()
    await flushPromises()
    const first = FakeAudioContext.instances[0]!
    for (const source of [...first.bufferSources, ...first.oscillators]) source.onended?.()
    expect(first.bufferSources[0]!.disconnect).toHaveBeenCalledOnce()
    expect(first.oscillators[0]!.disconnect).toHaveBeenCalledOnce()

    audio.disposeCrapsAudio()
    expect(first.close).toHaveBeenCalledOnce()
    audio.playCrapsClick()
    await flushPromises()
    const second = FakeAudioContext.instances[1]!
    second.state = 'closed'
    audio.playCrapsRoll()
    await flushPromises()
    expect(FakeAudioContext.instances).toHaveLength(3)
    expect(FakeAudioContext.instances[2]!.bufferSources).toHaveLength(12)
  })

  it('plays a brighter winning cue and a restrained losing cue only after unlocking', async () => {
    const audio = await import('./crapsAudio')
    audio.playCrapsClick()
    await flushPromises()
    const context = FakeAudioContext.instances[0]!
    audio.playCrapsResult(false)
    expect(context.oscillators).toHaveLength(3)
    audio.playCrapsResult(true)
    expect(context.oscillators).toHaveLength(13)
    expect(context.oscillators[1]!.disconnect).toHaveBeenCalledOnce()
  })

  it.each([undefined, {}])('is safe without a browser AudioContext: %j', async browser => {
    vi.stubGlobal('window', browser)
    const audio = await import('./crapsAudio')
    expect(() => {
      audio.playCrapsClick()
      audio.playCrapsRoll()
      audio.playCrapsResult(true)
      audio.disposeCrapsAudio()
    }).not.toThrow()
    expect(FakeAudioContext.instances).toHaveLength(0)
  })

  it('supports the prefixed browser audio constructor', async () => {
    vi.stubGlobal('window', { webkitAudioContext: FakeAudioContext })
    const audio = await import('./crapsAudio')
    audio.playCrapsClick()
    await flushPromises()
    expect(FakeAudioContext.instances).toHaveLength(1)
  })

  it('treats a denied audio constructor as optional instead of breaking the game', async () => {
    vi.stubGlobal('window', { AudioContext: class { constructor() { throw new Error('denied') } } })
    const audio = await import('./crapsAudio')
    expect(() => audio.playCrapsRoll()).not.toThrow()
  })
})

async function flushPromises(): Promise<void> {
  for (let index = 0; index < 8; index++) await Promise.resolve()
}

class FakeAudioParam {
  setValueAtTime = vi.fn()
  exponentialRampToValueAtTime = vi.fn()
}
class FakeAudioNode {
  connect = vi.fn((destination: FakeAudioNode) => destination)
  disconnect = vi.fn()
}
class FakeGainNode extends FakeAudioNode { gain = new FakeAudioParam() }
class FakeFilterNode extends FakeAudioNode { type = 'lowpass'; frequency = new FakeAudioParam(); Q = new FakeAudioParam() }
class FakeScheduledSource extends FakeAudioNode {
  onended: (() => void) | null = null
  start = vi.fn((_when?: number, _offset?: number) => undefined)
  stop = vi.fn((_when?: number) => undefined)
}
class FakeOscillatorNode extends FakeScheduledSource { type = 'sine'; frequency = new FakeAudioParam() }
class FakeBufferSourceNode extends FakeScheduledSource { buffer: unknown = null; loop = false }

class FakeAudioContext {
  static instances: FakeAudioContext[] = []
  static autoResume = true
  static rejectResume = false
  readonly destination = new FakeAudioNode()
  readonly sampleRate = 8_000
  readonly currentTime = 1
  readonly oscillators: FakeOscillatorNode[] = []
  readonly bufferSources: FakeBufferSourceNode[] = []
  state: AudioContextState = 'suspended'
  private resumeResolver: (() => void) | null = null
  readonly resume = vi.fn(() => {
    if (FakeAudioContext.rejectResume) return Promise.reject(new Error('not allowed'))
    if (FakeAudioContext.autoResume) {
      this.state = 'running'
      return Promise.resolve()
    }
    return new Promise<void>(resolve => {
      this.resumeResolver = () => {
        this.state = 'running'
        resolve()
      }
    })
  })
  readonly close = vi.fn(() => { this.state = 'closed'; return Promise.resolve() })
  constructor() { FakeAudioContext.instances.push(this) }
  createGain = vi.fn(() => new FakeGainNode())
  createBiquadFilter = vi.fn(() => new FakeFilterNode())
  createOscillator = vi.fn(() => {
    const oscillator = new FakeOscillatorNode()
    this.oscillators.push(oscillator)
    return oscillator
  })
  createBufferSource = vi.fn(() => {
    const source = new FakeBufferSourceNode()
    this.bufferSources.push(source)
    return source
  })
  createBuffer = vi.fn((_channels: number, length: number, sampleRate: number) => ({
    sampleRate,
    getChannelData: () => new Float32Array(length),
  }))
  finishResume(): void { this.resumeResolver?.(); this.resumeResolver = null }
}
