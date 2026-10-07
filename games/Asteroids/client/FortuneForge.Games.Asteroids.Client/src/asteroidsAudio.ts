export type AsteroidsSound = 'laser' | 'explosion' | 'thrust'

let audioContext: AudioContext | null = null
let lastThrustAt = Number.NEGATIVE_INFINITY

export function playAsteroidsSound(sound: AsteroidsSound): void {
  const context = getAudioContext()
  if (context === null) return
  if (context.state === 'suspended') void context.resume()

  if (sound === 'laser') playLaser(context)
  else if (sound === 'explosion') playExplosion(context)
  else playThrust(context)
}

function getAudioContext(): AudioContext | null {
  if (typeof window === 'undefined') return null
  const AudioContextClass = window.AudioContext
  if (AudioContextClass === undefined) return null
  audioContext ??= new AudioContextClass()
  return audioContext
}

function playLaser(context: AudioContext): void {
  const now = context.currentTime
  const output = context.createGain()
  output.gain.setValueAtTime(0.0001, now)
  output.gain.exponentialRampToValueAtTime(0.16, now + 0.008)
  output.gain.exponentialRampToValueAtTime(0.0001, now + 0.15)
  output.connect(context.destination)

  for (const [offset, start, end] of [[0, 980, 310], [0.035, 620, 210]] as const) {
    const oscillator = context.createOscillator()
    oscillator.type = 'square'
    oscillator.frequency.setValueAtTime(start, now + offset)
    oscillator.frequency.exponentialRampToValueAtTime(end, now + offset + 0.11)
    oscillator.connect(output)
    oscillator.start(now + offset)
    oscillator.stop(now + offset + 0.12)
  }
}

function playExplosion(context: AudioContext): void {
  const now = context.currentTime
  const duration = 0.34
  const sampleCount = Math.floor(context.sampleRate * duration)
  const buffer = context.createBuffer(1, sampleCount, context.sampleRate)
  const samples = buffer.getChannelData(0)
  for (let index = 0; index < sampleCount; index += 1) {
    const fade = 1 - index / sampleCount
    samples[index] = (Math.random() * 2 - 1) * fade * fade
  }

  const noise = context.createBufferSource()
  const filter = context.createBiquadFilter()
  const output = context.createGain()
  noise.buffer = buffer
  filter.type = 'lowpass'
  filter.frequency.setValueAtTime(1400, now)
  filter.frequency.exponentialRampToValueAtTime(120, now + duration)
  output.gain.setValueAtTime(0.26, now)
  output.gain.exponentialRampToValueAtTime(0.0001, now + duration)
  noise.connect(filter)
  filter.connect(output)
  output.connect(context.destination)
  noise.start(now)
}

function playThrust(context: AudioContext): void {
  const now = context.currentTime
  if (now - lastThrustAt < 0.095) return
  lastThrustAt = now

  const oscillator = context.createOscillator()
  const output = context.createGain()
  oscillator.type = 'sawtooth'
  oscillator.frequency.setValueAtTime(78, now)
  oscillator.frequency.linearRampToValueAtTime(118, now + 0.08)
  output.gain.setValueAtTime(0.0001, now)
  output.gain.exponentialRampToValueAtTime(0.055, now + 0.012)
  output.gain.exponentialRampToValueAtTime(0.0001, now + 0.1)
  oscillator.connect(output)
  output.connect(context.destination)
  oscillator.start(now)
  oscillator.stop(now + 0.105)
}
