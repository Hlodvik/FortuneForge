type AudioWindow = Window & { webkitAudioContext?: typeof AudioContext }
type Cue = 'click' | 'roll' | 'result'
type Voice = {
  cue: Cue
  nodes: Set<AudioNode>
  sources: Set<AudioScheduledSourceNode>
}

const muteStorageKey = 'fortuneforge:craps:muted'
const silence = .0001
const voices = new Set<Voice>()

let muted = readStoredMute()
let context: AudioContext | null = null
let output: GainNode | null = null
let noise: AudioBuffer | null = null
let resumeRequest: Promise<void> | null = null
let pendingGestureCue: (() => void) | null = null
let generation = 0

export function getCrapsMuted(): boolean { return muted }

export function setCrapsMuted(next: boolean): void {
  muted = next
  try { localStorage.setItem(muteStorageKey, String(next)) } catch { /* Storage may be unavailable. */ }
  if (!next) return
  generation++
  pendingGestureCue = null
  resumeRequest = null
  for (const voice of voices) stopVoice(voice)
}

/** Call directly from a pointer/keyboard handler; this also unlocks browser audio. */
export function playCrapsClick(): void {
  playGestureCue('click', (audio, voice) => {
    const now = audio.currentTime
    noiseContact(audio, voice, now, .024, .075, 2_400)
    tone(audio, voice, now, 680, 350, .045, .025, 'triangle')
  })
}

/** Two dice tumbling, with a soft rolling bed and irregular, decaying contacts. */
export function playCrapsRoll(): void {
  playGestureCue('roll', (audio, voice) => {
    const now = audio.currentTime
    noiseContact(audio, voice, now, .68, .065, 850, 'lowpass', .025)
    const contacts = [0, .037, .084, .139, .201, .272, .349, .433, .524, .623, .72]
    for (let index = 0; index < contacts.length; index++) {
      const at = now + contacts[index]! + Math.random() * .009
      const strength = .95 - index * .052
      // Interleaving two different pitches avoids a metronomic, single-die rattle.
      const pitch = 165 + Math.random() * 100
      noiseContact(audio, voice, at, .025 + Math.random() * .016, .15 * strength, 1_400 + Math.random() * 1_300)
      tone(audio, voice, at, pitch, pitch * .56, .055, .045 * strength, 'sine')
      tone(audio, voice, at + .013, 870 + Math.random() * 400, 520, .022, .02 * strength, 'triangle')
    }
  })
}

/** Result effects never create or resume an audio context without a prior gesture. */
export function playCrapsResult(won: boolean): void {
  const audio = context
  if (muted || audio === null || audio.state !== 'running') return
  playSafely(audio, 'result', (runningAudio, voice) => {
    const now = runningAudio.currentTime
    if (won) {
      for (const [index, pitch] of [523.25, 659.25, 783.99, 1_046.5].entries()) {
        tone(runningAudio, voice, now + index * .075, pitch, pitch, .26, .045, 'sine')
        tone(runningAudio, voice, now + index * .075, pitch * 2, pitch * 2, .16, .008, 'triangle')
      }
      tone(runningAudio, voice, now + .29, 659.25, 659.25, .36, .025, 'sine')
      tone(runningAudio, voice, now + .29, 783.99, 783.99, .36, .025, 'sine')
    } else {
      tone(runningAudio, voice, now, 246.94, 220, .13, .025, 'sine')
      tone(runningAudio, voice, now + .1, 196, 174.61, .2, .02, 'sine')
    }
  })
}

/** Release scheduled sounds/context when leaving the game; the next gesture can reopen it. */
export function disposeCrapsAudio(): void {
  generation++
  pendingGestureCue = null
  resumeRequest = null
  for (const voice of voices) stopVoice(voice)
  const audio = context
  context = null
  output = null
  noise = null
  if (audio === null || audio.state === 'closed') return
  try { void audio.close().catch(() => undefined) } catch { /* Audio is optional. */ }
}

function readStoredMute(): boolean {
  try { return localStorage.getItem(muteStorageKey) === 'true' } catch { return false }
}

function ensureContext(): AudioContext | null {
  if (typeof window === 'undefined') return null
  if (context !== null && context.state !== 'closed') return context
  const AudioContextType = window.AudioContext ?? (window as AudioWindow).webkitAudioContext
  if (AudioContextType === undefined) return null
  for (const voice of voices) stopVoice(voice)
  context = null
  output = null
  noise = null
  let created: AudioContext | null = null
  try {
    created = new AudioContextType()
    const gain = created.createGain()
    gain.gain.setValueAtTime(.62, created.currentTime)
    gain.connect(created.destination)
    output = gain
    context = created
    return created
  } catch {
    try { void created?.close().catch(() => undefined) } catch { /* Audio is optional. */ }
    return null
  }
}

function playGestureCue(cue: Cue, play: (audio: AudioContext, voice: Voice) => void): void {
  if (muted) return
  const audio = ensureContext()
  if (audio === null) return
  if (audio.state === 'running') {
    pendingGestureCue = null
    playSafely(audio, cue, play)
    return
  }
  // A suspended tab must not replay a pile of obsolete button/roll effects on resume.
  pendingGestureCue = () => playSafely(audio, cue, play)
  if (resumeRequest !== null) return
  const currentGeneration = generation
  try {
    const request = audio.resume()
    resumeRequest = request
    void request.then(() => {
      if (muted || generation !== currentGeneration || context !== audio || audio.state !== 'running') return
      const pending = pendingGestureCue
      pendingGestureCue = null
      pending?.()
    }).catch(() => {
      if (generation === currentGeneration) pendingGestureCue = null
    }).finally(() => {
      if (resumeRequest === request) resumeRequest = null
    })
  } catch {
    pendingGestureCue = null
    resumeRequest = null
  }
}

function playSafely(audio: AudioContext, cue: Cue, play: (audio: AudioContext, voice: Voice) => void): void {
  if (muted || context !== audio || audio.state !== 'running' || output === null) return
  const sameCue = [...voices].filter(voice => voice.cue === cue)
  const limit = cue === 'click' ? 4 : 1
  while (sameCue.length >= limit) stopVoice(sameCue.shift()!)
  const voice: Voice = { cue, nodes: new Set(), sources: new Set() }
  voices.add(voice)
  try { play(audio, voice) } catch { stopVoice(voice) }
}

function own<T extends AudioNode>(voice: Voice, node: T): T {
  voice.nodes.add(node)
  return node
}

function ownSource<T extends AudioScheduledSourceNode>(voice: Voice, source: T): T {
  own(voice, source)
  voice.sources.add(source)
  source.onended = () => {
    source.onended = null
    voice.sources.delete(source)
    if (voice.sources.size === 0) stopVoice(voice)
  }
  return source
}

function stopVoice(voice: Voice): void {
  voices.delete(voice)
  for (const source of voice.sources) {
    source.onended = null
    try { source.stop() } catch { /* Already ended. */ }
  }
  voice.sources.clear()
  for (const node of voice.nodes) {
    try { node.disconnect() } catch { /* Already disconnected. */ }
  }
  voice.nodes.clear()
}

function tone(audio: AudioContext, voice: Voice, start: number, from: number, to: number, duration: number, volume: number, type: OscillatorType): void {
  const oscillator = ownSource(voice, audio.createOscillator())
  const gain = own(voice, audio.createGain())
  oscillator.type = type
  oscillator.frequency.setValueAtTime(from, start)
  oscillator.frequency.exponentialRampToValueAtTime(to, start + duration)
  envelope(gain.gain, start, duration, volume)
  oscillator.connect(gain).connect(output!)
  oscillator.start(start)
  oscillator.stop(start + duration + .008)
}

function noiseContact(audio: AudioContext, voice: Voice, start: number, duration: number, volume: number, frequency: number, filterType: BiquadFilterType = 'bandpass', attack = .002): void {
  const source = ownSource(voice, audio.createBufferSource())
  const filter = own(voice, audio.createBiquadFilter())
  const gain = own(voice, audio.createGain())
  source.buffer = noiseBuffer(audio)
  source.loop = true
  filter.type = filterType
  filter.frequency.setValueAtTime(frequency, start)
  filter.frequency.exponentialRampToValueAtTime(frequency * .65, start + duration)
  filter.Q.setValueAtTime(.65, start)
  envelope(gain.gain, start, duration, volume, attack)
  source.connect(filter).connect(gain).connect(output!)
  source.start(start, Math.random() * .1)
  source.stop(start + duration + .008)
}

function envelope(gain: AudioParam, start: number, duration: number, volume: number, attack = .003): void {
  gain.setValueAtTime(silence, start)
  gain.exponentialRampToValueAtTime(volume, start + attack)
  gain.exponentialRampToValueAtTime(silence, start + duration)
}

function noiseBuffer(audio: AudioContext): AudioBuffer {
  if (noise !== null) return noise
  noise = audio.createBuffer(1, Math.ceil(audio.sampleRate * .8), audio.sampleRate)
  const samples = noise.getChannelData(0)
  for (let index = 0; index < samples.length; index++) samples[index] = Math.random() * 2 - 1
  return noise
}
