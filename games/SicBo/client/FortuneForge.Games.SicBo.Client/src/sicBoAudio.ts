type AudioWindow = Window & typeof globalThis & { webkitAudioContext?: typeof AudioContext }

export type SicBoAudioCue = 'click' | 'roll'

let context: AudioContext | null = null

function audioContext(): AudioContext | null {
  if (typeof window === 'undefined') return null
  const AudioContextType = window.AudioContext ?? (window as AudioWindow).webkitAudioContext
  if (!AudioContextType) return null
  context ??= new AudioContextType()
  if (context.state === 'suspended') void context.resume()
  return context
}

function strike(
  audio: AudioContext,
  start: number,
  startFrequency: number,
  endFrequency: number,
  duration: number,
  volume: number,
  type: OscillatorType,
) {
  const oscillator = audio.createOscillator()
  const gain = audio.createGain()
  oscillator.type = type
  oscillator.frequency.setValueAtTime(startFrequency, start)
  oscillator.frequency.exponentialRampToValueAtTime(endFrequency, start + duration)
  gain.gain.setValueAtTime(0.0001, start)
  gain.gain.exponentialRampToValueAtTime(volume, start + 0.004)
  gain.gain.exponentialRampToValueAtTime(0.0001, start + duration)
  oscillator.connect(gain).connect(audio.destination)
  oscillator.start(start)
  oscillator.stop(start + duration + 0.01)
}

function click(audio: AudioContext) {
  const start = audio.currentTime
  strike(audio, start, 310, 185, 0.055, 0.026, 'triangle')
  strike(audio, start, 760, 430, 0.026, 0.009, 'sine')
}

function roll(audio: AudioContext) {
  const start = audio.currentTime
  const impacts = [
    [0, 138, 76, 0.052, 0.034],
    [0.052, 108, 68, 0.045, 0.027],
    [0.101, 162, 81, 0.05, 0.036],
    [0.158, 119, 65, 0.044, 0.026],
    [0.211, 151, 73, 0.052, 0.035],
    [0.273, 105, 62, 0.046, 0.027],
    [0.334, 143, 70, 0.058, 0.037],
    [0.405, 91, 55, 0.07, 0.031],
  ] as const

  impacts.forEach(([offset, high, low, duration, volume], index) => {
    strike(audio, start + offset, high, low, duration, volume, 'triangle')
    strike(audio, start + offset + 0.002, high * 4.6, low * 3.2, duration * 0.55, volume * 0.34, index % 2 ? 'sine' : 'square')
  })
}

export function playSicBoSound(cue: SicBoAudioCue): void {
  const audio = audioContext()
  if (!audio) return
  if (cue === 'roll') roll(audio)
  else click(audio)
}
