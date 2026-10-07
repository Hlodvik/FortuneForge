import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

describe('Asteroids audio controller', () => {
  beforeEach(() => {
    vi.resetModules()
    vi.useFakeTimers()
    FakeAudioContext.instances = []
    FakeAudioContext.autoResume = true
    const stored = new Map<string, string>()
    vi.stubGlobal('localStorage', {
      getItem: (key: string) => stored.get(key) ?? null,
      setItem: (key: string, value: string) => stored.set(key, value),
    })
    vi.stubGlobal('window', { AudioContext: FakeAudioContext })
  })

  afterEach(async () => {
    const audio = await import('./asteroidsAudio')
    audio.stopAsteroidsAudioScene()
    vi.clearAllTimers()
    vi.useRealTimers()
    vi.unstubAllGlobals()
  })

  it('does not create or play audio before a browser gesture unlocks it', async () => {
    const audio = await import('./asteroidsAudio')

    audio.playAsteroidsSound('player-shot')
    expect(FakeAudioContext.instances).toHaveLength(0)

    audio.unlockAsteroidsAudio()
    await flushPromises()
    audio.playAsteroidsSound('player-shot')

    expect(FakeAudioContext.instances).toHaveLength(1)
    expect(FakeAudioContext.instances[0]!.resume).toHaveBeenCalledOnce()
    expect(FakeAudioContext.instances[0]!.oscillators.length).toBeGreaterThan(0)
  })

  it('honours a persisted mute without creating an AudioContext', async () => {
    localStorage.setItem('fortuneforge:asteroids:muted', 'true')
    vi.resetModules()
    const audio = await import('./asteroidsAudio')

    audio.unlockAsteroidsAudio()
    audio.playAsteroidsSound('game-over')

    expect(audio.isAsteroidsAudioMuted()).toBe(true)
    expect(FakeAudioContext.instances).toHaveLength(0)
  })

  it('keeps thrust as one continuous loop and fades it when released', async () => {
    const audio = await import('./asteroidsAudio')
    audio.unlockAsteroidsAudio()
    await flushPromises()

    audio.setAsteroidsThrusting(true)
    audio.setAsteroidsThrusting(true)
    const context = FakeAudioContext.instances[0]!
    expect(context.oscillators).toHaveLength(1)
    expect(context.bufferSources).toHaveLength(1)

    audio.setAsteroidsThrusting(false)
    expect(context.oscillators[0]!.stop).toHaveBeenCalledOnce()
    expect(context.bufferSources[0]!.stop).toHaveBeenCalledOnce()
  })

  it('stops the reactive heartbeat when its scene ends', async () => {
    const audio = await import('./asteroidsAudio')
    audio.unlockAsteroidsAudio()
    await flushPromises()
    const context = FakeAudioContext.instances[0]!

    audio.setAsteroidsAudioScene({ playing: true, urgency: 1 })
    vi.advanceTimersByTime(121)
    const beats = context.oscillators.length
    expect(beats).toBeGreaterThan(0)

    audio.stopAsteroidsAudioScene()
    vi.advanceTimersByTime(2_000)
    expect(context.oscillators).toHaveLength(beats)
  })

  it('invalidates suspended cues and loops when the scene is left before audio resumes', async () => {
    FakeAudioContext.autoResume = false
    const audio = await import('./asteroidsAudio')
    audio.unlockAsteroidsAudio()
    const context = FakeAudioContext.instances[0]!

    audio.playAsteroidsSound('alien-fired')
    audio.setAsteroidsThrusting(true)
    audio.setAsteroidsAudioScene({ playing: true, urgency: 1, alien: { type: 'hunter', pan: .5 } })
    vi.advanceTimersByTime(121)
    audio.stopAsteroidsAudioScene()
    context.finishResume()
    await flushPromises()

    expect(context.oscillators).toHaveLength(0)
    expect(context.bufferSources).toHaveLength(0)
  })

  it('coalesces suspended one-shot cues instead of replaying a stale burst', async () => {
    FakeAudioContext.autoResume = false
    const audio = await import('./asteroidsAudio')
    audio.unlockAsteroidsAudio()
    const context = FakeAudioContext.instances[0]!

    audio.playAsteroidsSound('player-shot')
    audio.playAsteroidsSound('asteroid-destroyed')
    audio.playAsteroidsSound('alien-fired')
    context.finishResume()
    await flushPromises()

    expect(context.oscillators).toHaveLength(2)
    expect(context.bufferSources).toHaveLength(0)
  })

  it('keeps a suspended terminal cue through scene teardown', async () => {
    FakeAudioContext.autoResume = false
    const audio = await import('./asteroidsAudio')
    audio.unlockAsteroidsAudio()
    const context = FakeAudioContext.instances[0]!

    audio.playAsteroidsSound('game-over')
    audio.stopAsteroidsAudioScene()
    context.finishResume()
    await flushPromises()

    expect(context.oscillators).toHaveLength(2)
    expect(context.bufferSources).toHaveLength(1)
  })
})

async function flushPromises(): Promise<void> {
  for (let index = 0; index < 8; index += 1) await Promise.resolve()
}

class FakeAudioParam {
  value = 1
  setValueAtTime = vi.fn((value: number) => { this.value = value })
  linearRampToValueAtTime = vi.fn((value: number) => { this.value = value })
  exponentialRampToValueAtTime = vi.fn((value: number) => { this.value = value })
  setTargetAtTime = vi.fn((value: number) => { this.value = value })
  cancelScheduledValues = vi.fn()
}

class FakeAudioNode {
  connect = vi.fn((destination: FakeAudioNode) => destination)
  disconnect = vi.fn()
}

class FakeGainNode extends FakeAudioNode { gain = new FakeAudioParam() }
class FakeFilterNode extends FakeAudioNode { type = 'lowpass'; frequency = new FakeAudioParam(); Q = new FakeAudioParam() }
class FakePannerNode extends FakeAudioNode { pan = new FakeAudioParam() }
class FakeCompressorNode extends FakeAudioNode {
  threshold = new FakeAudioParam()
  knee = new FakeAudioParam()
  ratio = new FakeAudioParam()
  attack = new FakeAudioParam()
  release = new FakeAudioParam()
}
class FakeScheduledSource extends FakeAudioNode {
  onended: (() => void) | null = null
  start = vi.fn()
  stop = vi.fn()
}
class FakeOscillatorNode extends FakeScheduledSource {
  type = 'sine'
  frequency = new FakeAudioParam()
  detune = new FakeAudioParam()
}
class FakeBufferSourceNode extends FakeScheduledSource { buffer: unknown = null; loop = false }

class FakeAudioContext {
  static instances: FakeAudioContext[] = []
  static autoResume = true
  readonly destination = new FakeAudioNode()
  readonly sampleRate = 8_000
  readonly oscillators: FakeOscillatorNode[] = []
  readonly bufferSources: FakeBufferSourceNode[] = []
  readonly currentTime = 1
  state: AudioContextState = 'suspended'
  private resumeResolver: (() => void) | null = null
  readonly resume = vi.fn(() => {
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

  constructor() { FakeAudioContext.instances.push(this) }
  createGain = vi.fn(() => new FakeGainNode())
  createBiquadFilter = vi.fn(() => new FakeFilterNode())
  createStereoPanner = vi.fn(() => new FakePannerNode())
  createDynamicsCompressor = vi.fn(() => new FakeCompressorNode())
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
    duration: length / sampleRate,
    sampleRate,
    getChannelData: () => new Float32Array(length),
  }))
  finishResume(): void {
    this.resumeResolver?.()
    this.resumeResolver = null
  }
}
