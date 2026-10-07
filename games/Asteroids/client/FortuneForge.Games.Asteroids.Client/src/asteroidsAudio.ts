import type { AsteroidSize, AsteroidsPowerUpType } from './contracts'

export type AsteroidsSound =
  | 'mission-start'
  | 'player-shot'
  | 'asteroid-hit'
  | 'asteroid-destroyed'
  | 'ship-damaged'
  | 'game-over'
  | 'power-up-spawned'
  | 'power-up-collected'
  | 'wave-cleared'
  | 'time-up'
  | 'alien-entered'
  | 'alien-fired'
  | 'alien-hit'
  | 'alien-destroyed'

export type AsteroidsAlienSoundType = 'scout' | 'hunter'
export type AsteroidsSoundOptions = Readonly<{
  pan?: number
  size?: AsteroidSize
  powerUp?: AsteroidsPowerUpType
  rapidFire?: boolean
  alienType?: AsteroidsAlienSoundType
}>

export type AsteroidsAudioScene = Readonly<{
  playing: boolean
  urgency: number
  alien?: Readonly<{ type: AsteroidsAlienSoundType; pan: number }> | null
}>

type AudioWindow = Window & typeof globalThis & { webkitAudioContext?: typeof AudioContext }
type ActiveLoop = Readonly<{ sources: readonly AudioScheduledSourceNode[]; gains: readonly GainNode[]; panner?: StereoPannerNode }>
type PendingOneShot = Readonly<{
  audio: AudioContext
  generation: number
  play: (audio: AudioContext) => void
  survivesSceneStop: boolean
}>

const muteStorageKey = 'fortuneforge:asteroids:muted'
const silence = 0.0001

let context: AudioContext | null = null
let master: GainNode | null = null
let effects: GainNode | null = null
let ambience: GainNode | null = null
let noise: AudioBuffer | null = null
let resumeRequest: Promise<void> | null = null
let activated = false
let muted = readStoredMute()
let thrustLoop: ActiveLoop | null = null
let alienLoop: ActiveLoop | null = null
let alienLoopType: AsteroidsAlienSoundType | null = null
let heartbeatTimer: ReturnType<typeof setTimeout> | null = null
let heartbeatActive = false
let heartbeatUrgency = 0
let heartbeatPhase = false
let thrustRequested = false
let desiredAlien: AsteroidsAudioScene['alien'] = null
let audioGeneration = 0
let heartbeatResumePending = false
let pendingOneShot: PendingOneShot | null = null
let oneShotResumePending = false

/** Must be called from a pointer or keyboard handler before frame-driven cues can play. */
export function unlockAsteroidsAudio(): void {
  activated = true
  if (muted) return
  const audio = ensureAudioContext()
  if (audio === null) return
  void resumeAudio(audio).then(refreshContinuousAudio).catch(() => undefined)
}

export function isAsteroidsAudioMuted(): boolean { return muted }

export function setAsteroidsAudioMuted(next: boolean): void {
  muted = next
  try { localStorage.setItem(muteStorageKey, String(next)) } catch { /* storage is optional */ }

  if (master !== null && context !== null) {
    const now = context.currentTime
    master.gain.cancelScheduledValues(now)
    master.gain.setValueAtTime(Math.max(silence, master.gain.value), now)
    master.gain.exponentialRampToValueAtTime(next ? silence : .72, now + .035)
  }
  if (next) {
    audioGeneration++
    pendingOneShot = null
    stopContinuousAudio()
  }
  else unlockAsteroidsAudio()
}

export function playAsteroidsSound(sound: AsteroidsSound, options: AsteroidsSoundOptions = {}): void {
  playOneShotWhenRunning(audio => {
    const pan = clamp(options.pan ?? 0, -.72, .72)
    switch (sound) {
      case 'mission-start': playMissionStart(audio); break
      case 'player-shot': playPlayerShot(audio, pan, options.rapidFire === true); break
      case 'asteroid-hit': playAsteroidHit(audio, pan, options.size ?? 'medium'); break
      case 'asteroid-destroyed': playAsteroidDestroyed(audio, pan, options.size ?? 'medium'); break
      case 'ship-damaged': playShipDamage(audio, false); break
      case 'game-over': playShipDamage(audio, true); break
      case 'power-up-spawned': playPowerUpSpawn(audio, pan); break
      case 'power-up-collected': playPowerUpCollected(audio, options.powerUp ?? 'shield'); break
      case 'wave-cleared': playWaveCleared(audio); break
      case 'time-up': playTimeUp(audio); break
      case 'alien-entered': playAlienEntered(audio, options.alienType ?? 'scout'); break
      case 'alien-fired': playAlienShot(audio, pan, options.alienType ?? 'scout'); break
      case 'alien-hit': playAlienHit(audio, pan); break
      case 'alien-destroyed': playAlienDestroyed(audio, pan, options.alienType ?? 'scout'); break
    }
  }, sound === 'game-over' || sound === 'time-up')
}

export function setAsteroidsThrusting(active: boolean): void {
  thrustRequested = active
  if (!active) {
    fadeLoop(thrustLoop, .11)
    thrustLoop = null
    return
  }
  withRunningAudio(audio => {
    if (!thrustRequested || thrustLoop !== null || muted) return
    const now = audio.currentTime
    const loopNoise = audio.createBufferSource()
    const rumble = audio.createOscillator()
    const filter = audio.createBiquadFilter()
    const gain = audio.createGain()
    loopNoise.buffer = noiseBuffer(audio)
    loopNoise.loop = true
    rumble.type = 'sawtooth'
    rumble.frequency.setValueAtTime(52, now)
    filter.type = 'lowpass'
    filter.frequency.setValueAtTime(430, now)
    filter.Q.setValueAtTime(.7, now)
    gain.gain.setValueAtTime(silence, now)
    gain.gain.exponentialRampToValueAtTime(.052, now + .045)
    loopNoise.connect(filter)
    rumble.connect(filter)
    filter.connect(gain).connect(ambienceBus(audio))
    loopNoise.start(now)
    rumble.start(now)
    thrustLoop = { sources: [loopNoise, rumble], gains: [gain] }
  })
}

export function setAsteroidsAudioScene(scene: AsteroidsAudioScene): void {
  const wasPlaying = heartbeatActive
  heartbeatActive = scene.playing
  heartbeatUrgency = clamp(scene.urgency, 0, 1)
  syncAlienLoop(scene.playing ? scene.alien ?? null : null)
  if (!scene.playing) {
    if (wasPlaying) audioGeneration++
    setAsteroidsThrusting(false)
    stopHeartbeat()
  } else if (activated && !muted) {
    scheduleHeartbeat()
  }
}

/** Stops scene-owned loops while keeping the user-unlocked context reusable. */
export function stopAsteroidsAudioScene(): void {
  audioGeneration++
  heartbeatActive = false
  stopHeartbeat()
  setAsteroidsThrusting(false)
  syncAlienLoop(null)
}

function ensureAudioContext(): AudioContext | null {
  if (typeof window === 'undefined') return null
  const AudioContextType = window.AudioContext ?? (window as AudioWindow).webkitAudioContext
  if (AudioContextType === undefined) return null
  if (context !== null && context.state !== 'closed') return context

  context = new AudioContextType()
  const compressor = context.createDynamicsCompressor()
  master = context.createGain()
  effects = context.createGain()
  ambience = context.createGain()
  compressor.threshold.setValueAtTime(-12, context.currentTime)
  compressor.knee.setValueAtTime(14, context.currentTime)
  compressor.ratio.setValueAtTime(7, context.currentTime)
  compressor.attack.setValueAtTime(.004, context.currentTime)
  compressor.release.setValueAtTime(.16, context.currentTime)
  master.gain.setValueAtTime(muted ? silence : .72, context.currentTime)
  effects.gain.setValueAtTime(1, context.currentTime)
  ambience.gain.setValueAtTime(.82, context.currentTime)
  effects.connect(master)
  ambience.connect(master)
  master.connect(compressor).connect(context.destination)
  return context
}

function withRunningAudio(play: (audio: AudioContext) => void): void {
  if (!activated || muted) return
  const generation = audioGeneration
  const audio = context
  if (audio === null || audio.state === 'closed') return
  if (audio.state === 'running') {
    play(audio)
    return
  }
  void resumeAudio(audio).then(() => { if (!muted && audio.state === 'running' && generation === audioGeneration) play(audio) }).catch(() => undefined)
}

/**
 * One-shot frame cues are intentionally coalesced while the browser has the
 * context suspended. Replaying every obsolete hit and shot after a tab resumes
 * creates a loud burst; the newest cue is the only one still useful. Terminal
 * cues survive scene teardown so game-over/time-up is not cancelled by the
 * completion render that emitted it.
 */
function playOneShotWhenRunning(play: (audio: AudioContext) => void, survivesSceneStop: boolean): void {
  if (!activated || muted) return
  const audio = context
  if (audio === null || audio.state === 'closed') return
  if (audio.state === 'running') {
    play(audio)
    return
  }

  pendingOneShot = { audio, generation: audioGeneration, play, survivesSceneStop }
  if (oneShotResumePending) return
  oneShotResumePending = true
  void resumeAudio(audio).then(() => {
    oneShotResumePending = false
    const pending = pendingOneShot
    pendingOneShot = null
    if (pending === null || pending.audio !== audio || muted || audio.state !== 'running') return
    if (!pending.survivesSceneStop && pending.generation !== audioGeneration) return
    pending.play(audio)
  }).catch(() => {
    oneShotResumePending = false
    pendingOneShot = null
  })
}

function resumeAudio(audio: AudioContext): Promise<void> {
  if (audio.state === 'running') return Promise.resolve()
  resumeRequest ??= audio.resume().then(() => undefined).finally(() => { resumeRequest = null })
  return resumeRequest
}

function effectsBus(audio: AudioContext): GainNode { return effects ?? initialiseFallbackBus(audio, 'effects') }
function ambienceBus(audio: AudioContext): GainNode { return ambience ?? initialiseFallbackBus(audio, 'ambience') }
function initialiseFallbackBus(audio: AudioContext, kind: 'effects' | 'ambience'): GainNode {
  const bus = audio.createGain()
  bus.connect(audio.destination)
  if (kind === 'effects') effects = bus
  else ambience = bus
  return bus
}

function playMissionStart(audio: AudioContext): void {
  const now = audio.currentTime
  tone(audio, 190, 310, now, .16, .042, 'triangle')
  tone(audio, 310, 620, now + .11, .24, .05, 'sine')
}

function playPlayerShot(audio: AudioContext, pan: number, rapid: boolean): void {
  const now = audio.currentTime
  const duration = rapid ? .075 : .12
  tone(audio, rapid ? 1180 : 980, rapid ? 390 : 280, now, duration, rapid ? .065 : .095, 'square', pan)
  tone(audio, rapid ? 720 : 610, rapid ? 290 : 205, now + .018, duration * .72, rapid ? .025 : .038, 'triangle', pan)
}

function playAsteroidHit(audio: AudioContext, pan: number, size: AsteroidSize): void {
  const now = audio.currentTime
  const scale = sizeScale(size)
  noiseBurst(audio, now, .045 + scale * .018, 1250 - scale * 150, 310, .032 + scale * .006, pan, 'bandpass')
  tone(audio, 330 - scale * 35, 180 - scale * 15, now, .07, .022, 'triangle', pan)
}

function playAsteroidDestroyed(audio: AudioContext, pan: number, size: AsteroidSize): void {
  const now = audio.currentTime
  const scale = sizeScale(size)
  const duration = .2 + scale * .065
  noiseBurst(audio, now, duration, 1800 - scale * 210, 120, .105 + scale * .022, pan, 'lowpass')
  tone(audio, 150 - scale * 13, 46, now, duration * .9, .04 + scale * .012, 'sine', pan)
  if (scale >= 3) noiseBurst(audio, now + .055, duration * .55, 620, 90, .045, pan, 'bandpass')
}

function playShipDamage(audio: AudioContext, terminal: boolean): void {
  const now = audio.currentTime
  noiseBurst(audio, now, terminal ? .68 : .43, 2100, 85, terminal ? .21 : .16, 0, 'lowpass')
  tone(audio, terminal ? 190 : 260, 42, now, terminal ? .62 : .35, terminal ? .12 : .085, 'sawtooth')
  if (terminal) {
    tone(audio, 330, 92, now + .24, .48, .055, 'square')
    stopContinuousAudio(.12)
  }
}

function playPowerUpSpawn(audio: AudioContext, pan: number): void {
  const now = audio.currentTime + .075
  tone(audio, 720, 1080, now, .1, .025, 'sine', pan)
  tone(audio, 1050, 1420, now + .09, .12, .02, 'triangle', pan)
}

function playPowerUpCollected(audio: AudioContext, type: AsteroidsPowerUpType): void {
  const now = audio.currentTime
  if (type === 'shield') {
    tone(audio, 190, 780, now, .32, .055, 'sine')
    tone(audio, 380, 1040, now + .045, .27, .025, 'triangle')
  } else if (type === 'rapid-fire') {
    for (let index = 0; index < 4; index += 1) tone(audio, 420 + index * 120, 720 + index * 150, now + index * .045, .085, .034, 'square')
  } else {
    for (const [index, frequency] of [523, 659, 988].entries()) tone(audio, frequency, frequency * 1.04, now + index * .09, .18, .045, 'triangle')
  }
}

function playWaveCleared(audio: AudioContext): void {
  const now = audio.currentTime + .12
  tone(audio, 330, 440, now, .2, .038, 'triangle')
  tone(audio, 440, 660, now + .12, .24, .046, 'sine')
  tone(audio, 660, 880, now + .25, .27, .032, 'triangle')
}

function playTimeUp(audio: AudioContext): void {
  const now = audio.currentTime
  tone(audio, 620, 420, now, .18, .04, 'triangle')
  tone(audio, 420, 210, now + .16, .28, .048, 'sine')
  stopContinuousAudio(.18)
}

function playAlienEntered(audio: AudioContext, type: AsteroidsAlienSoundType): void {
  const now = audio.currentTime
  const high = type === 'hunter' ? 510 : 720
  tone(audio, high, high * .68, now, .16, .045, 'square')
  tone(audio, high * .78, high * .48, now + .13, .2, .04, 'square')
}

function playAlienShot(audio: AudioContext, pan: number, type: AsteroidsAlienSoundType): void {
  const now = audio.currentTime
  const start = type === 'hunter' ? 420 : 620
  tone(audio, start, start * 1.7, now, .12, .072, 'sawtooth', pan)
  tone(audio, start * .55, start * 1.08, now + .012, .09, .025, 'square', pan)
}

function playAlienHit(audio: AudioContext, pan: number): void {
  const now = audio.currentTime
  noiseBurst(audio, now, .085, 2800, 900, .055, pan, 'bandpass')
  tone(audio, 860, 430, now, .1, .032, 'square', pan)
}

function playAlienDestroyed(audio: AudioContext, pan: number, type: AsteroidsAlienSoundType): void {
  const now = audio.currentTime
  noiseBurst(audio, now, type === 'hunter' ? .5 : .34, 2400, 95, type === 'hunter' ? .17 : .13, pan, 'lowpass')
  tone(audio, type === 'hunter' ? 410 : 560, 58, now, type === 'hunter' ? .48 : .32, .085, 'square', pan)
  tone(audio, 980, 180, now + .035, .24, .036, 'sawtooth', pan)
}

function tone(audio: AudioContext, startFrequency: number, endFrequency: number, start: number, duration: number, volume: number, type: OscillatorType, pan = 0, bus: 'effects' | 'ambience' = 'effects'): void {
  const oscillator = audio.createOscillator()
  const gain = audio.createGain()
  const panner = createPanner(audio, pan)
  oscillator.type = type
  oscillator.frequency.setValueAtTime(Math.max(1, startFrequency), start)
  oscillator.frequency.exponentialRampToValueAtTime(Math.max(1, endFrequency), start + duration)
  gain.gain.setValueAtTime(silence, start)
  gain.gain.exponentialRampToValueAtTime(volume, start + Math.min(.009, duration * .2))
  gain.gain.exponentialRampToValueAtTime(silence, start + duration)
  oscillator.connect(gain)
  connectOutput(gain, panner, bus === 'effects' ? effectsBus(audio) : ambienceBus(audio))
  oscillator.onended = () => disconnectAll(oscillator, gain, panner)
  oscillator.start(start)
  oscillator.stop(start + duration + .012)
}

function noiseBurst(audio: AudioContext, start: number, duration: number, startFrequency: number, endFrequency: number, volume: number, pan: number, filterType: BiquadFilterType): void {
  const source = audio.createBufferSource()
  const filter = audio.createBiquadFilter()
  const gain = audio.createGain()
  const panner = createPanner(audio, pan)
  source.buffer = noiseBuffer(audio)
  filter.type = filterType
  filter.frequency.setValueAtTime(Math.max(40, startFrequency), start)
  filter.frequency.exponentialRampToValueAtTime(Math.max(40, endFrequency), start + duration)
  filter.Q.setValueAtTime(filterType === 'bandpass' ? 1.4 : .65, start)
  gain.gain.setValueAtTime(volume, start)
  gain.gain.exponentialRampToValueAtTime(silence, start + duration)
  source.connect(filter).connect(gain)
  connectOutput(gain, panner, effectsBus(audio))
  source.onended = () => disconnectAll(source, filter, gain, panner)
  const maximumOffset = Math.max(0, noiseBuffer(audio).duration - duration - .01)
  source.start(start, maximumOffset * Math.random(), duration)
}

function noiseBuffer(audio: AudioContext): AudioBuffer {
  if (noise !== null && noise.sampleRate === audio.sampleRate) return noise
  const sampleCount = Math.floor(audio.sampleRate * 2)
  noise = audio.createBuffer(1, sampleCount, audio.sampleRate)
  const samples = noise.getChannelData(0)
  let last = 0
  for (let index = 0; index < sampleCount; index += 1) {
    const white = Math.random() * 2 - 1
    last = last * .82 + white * .18
    samples[index] = last * .9
  }
  return noise
}

function syncAlienLoop(alien: AsteroidsAudioScene['alien']): void {
  desiredAlien = alien
  if (alien === null || alien === undefined) {
    fadeLoop(alienLoop, .14)
    alienLoop = null
    alienLoopType = null
    return
  }
  withRunningAudio(audio => {
    if (desiredAlien === null || desiredAlien === undefined || desiredAlien.type !== alien.type || muted) return
    if (alienLoop !== null && alienLoopType !== alien.type) {
      fadeLoop(alienLoop, .08)
      alienLoop = null
    }
    if (alienLoop === null) alienLoop = startAlienLoop(audio, alien.type, desiredAlien.pan)
    else if (alienLoop.panner !== undefined) alienLoop.panner.pan.setTargetAtTime(clamp(desiredAlien.pan, -.72, .72), audio.currentTime, .045)
    alienLoopType = alien.type
  })
}

function startAlienLoop(audio: AudioContext, type: AsteroidsAlienSoundType, pan: number): ActiveLoop {
  const now = audio.currentTime
  const low = audio.createOscillator()
  const high = audio.createOscillator()
  const gain = audio.createGain()
  const panner = createPanner(audio, pan)
  low.type = 'square'
  high.type = 'square'
  low.frequency.setValueAtTime(type === 'hunter' ? 86 : 118, now)
  high.frequency.setValueAtTime(type === 'hunter' ? 121 : 173, now)
  low.detune.setValueAtTime(-7, now)
  high.detune.setValueAtTime(8, now)
  gain.gain.setValueAtTime(silence, now)
  gain.gain.exponentialRampToValueAtTime(type === 'hunter' ? .042 : .034, now + .09)
  low.connect(gain)
  high.connect(gain)
  connectOutput(gain, panner, ambienceBus(audio))
  low.start(now)
  high.start(now)
  return { sources: [low, high], gains: [gain], panner }
}

function scheduleHeartbeat(): void {
  if (heartbeatTimer !== null || !heartbeatActive || muted || !activated) return
  heartbeatTimer = setTimeout(heartbeatBeat, 120)
}

function heartbeatBeat(): void {
  heartbeatTimer = null
  if (!heartbeatActive || muted || !activated) return
  const audio = context
  if (audio === null || audio.state === 'closed') return
  if (audio.state !== 'running') {
    if (heartbeatResumePending) return
    heartbeatResumePending = true
    const generation = audioGeneration
    void resumeAudio(audio).then(() => {
      heartbeatResumePending = false
      if (generation === audioGeneration && heartbeatActive && !muted && audio.state === 'running') playHeartbeatBeat(audio)
      if (heartbeatActive && !muted && activated) scheduleHeartbeat()
    }).catch(() => { heartbeatResumePending = false })
    return
  }
  playHeartbeatBeat(audio)
  heartbeatTimer = setTimeout(heartbeatBeat, 820 - heartbeatUrgency * 535)
}

function playHeartbeatBeat(audio: AudioContext): void {
    const now = audio.currentTime
    heartbeatPhase = !heartbeatPhase
    const frequency = heartbeatPhase ? 58 : 48
    tone(audio, frequency, frequency * .72, now, .105, .028 + heartbeatUrgency * .013, 'sine', 0, 'ambience')
}

function stopHeartbeat(): void {
  if (heartbeatTimer !== null) clearTimeout(heartbeatTimer)
  heartbeatTimer = null
}

function refreshContinuousAudio(): void {
  if (muted) return
  if (heartbeatActive) scheduleHeartbeat()
  if (thrustRequested) setAsteroidsThrusting(true)
  if (desiredAlien !== null && desiredAlien !== undefined) syncAlienLoop(desiredAlien)
}

function stopContinuousAudio(fadeSeconds = .06): void {
  stopHeartbeat()
  fadeLoop(thrustLoop, fadeSeconds)
  fadeLoop(alienLoop, fadeSeconds)
  thrustLoop = null
  alienLoop = null
  alienLoopType = null
}

function fadeLoop(loop: ActiveLoop | null, duration: number): void {
  if (loop === null || context === null) return
  const now = context.currentTime
  for (const gain of loop.gains) {
    gain.gain.cancelScheduledValues(now)
    gain.gain.setValueAtTime(Math.max(silence, gain.gain.value), now)
    gain.gain.exponentialRampToValueAtTime(silence, now + duration)
  }
  for (const source of loop.sources) {
    source.onended = () => disconnectAll(...loop.sources, ...loop.gains, loop.panner)
    try { source.stop(now + duration + .015) } catch { /* an ended source is already stopped */ }
  }
}

function createPanner(audio: AudioContext, pan: number): StereoPannerNode | undefined {
  if (typeof audio.createStereoPanner !== 'function') return undefined
  const panner = audio.createStereoPanner()
  panner.pan.setValueAtTime(clamp(pan, -.72, .72), audio.currentTime)
  return panner
}

function connectOutput(node: AudioNode, panner: StereoPannerNode | undefined, destination: AudioNode): void {
  if (panner === undefined) node.connect(destination)
  else node.connect(panner).connect(destination)
}

function disconnectAll(...nodes: readonly (AudioNode | undefined)[]): void {
  for (const node of nodes) {
    try { node?.disconnect() } catch { /* already disconnected */ }
  }
}

function sizeScale(size: AsteroidSize): number {
  return size === 'tiny' ? 0 : size === 'small' ? 1 : size === 'medium' ? 2 : size === 'large' ? 3 : 4
}

function readStoredMute(): boolean {
  try { return typeof localStorage !== 'undefined' && localStorage.getItem(muteStorageKey) === 'true' } catch { return false }
}

function clamp(value: number, minimum: number, maximum: number): number { return Math.min(maximum, Math.max(minimum, value)) }
