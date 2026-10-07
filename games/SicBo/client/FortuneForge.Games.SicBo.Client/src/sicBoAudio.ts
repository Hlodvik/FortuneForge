type AudioWindow = Window & typeof globalThis & { webkitAudioContext?: typeof AudioContext }

export type SicBoAudioCue = 'bet' | 'click' | 'roll' | 'win'

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

function bet(audio: AudioContext) {
  const start = audio.currentTime
  strike(audio, start, 230, 145, 0.075, 0.06, 'triangle')
  strike(audio, start, 720, 390, 0.038, 0.026, 'square')
  strike(audio, start + 0.026, 410, 265, 0.06, 0.038, 'triangle')
}

function roll(audio: AudioContext) {
  const start = audio.currentTime
  const duration = 0.56
  const frameCount = Math.max(1, Math.floor(audio.sampleRate * duration))
  const buffer = audio.createBuffer(1, frameCount, audio.sampleRate)
  const channel = buffer.getChannelData(0)
  for (let index = 0; index < frameCount; index += 1) {
    const progress = index / frameCount
    const envelope = Math.pow(1 - progress, 0.6)
    const rattle = 0.42 + Math.abs(Math.sin(progress * Math.PI * 19)) * 0.58
    channel[index] = (Math.random() * 2 - 1) * envelope * rattle
  }
  const source = audio.createBufferSource()
  const filter = audio.createBiquadFilter()
  const tumbleGain = audio.createGain()
  filter.type = 'bandpass'
  filter.frequency.setValueAtTime(520, start)
  filter.frequency.exponentialRampToValueAtTime(260, start + duration)
  filter.Q.value = 0.75
  tumbleGain.gain.setValueAtTime(0.0001, start)
  tumbleGain.gain.exponentialRampToValueAtTime(0.17, start + 0.018)
  tumbleGain.gain.exponentialRampToValueAtTime(0.0001, start + duration)
  source.connect(filter).connect(tumbleGain).connect(audio.destination)
  source.start(start)
  source.stop(start + duration)

  const impacts = [
    [0, 138, 76, 0.06, 0.055],
    [0.048, 108, 68, 0.052, 0.045],
    [0.098, 162, 81, 0.06, 0.06],
    [0.153, 119, 65, 0.052, 0.043],
    [0.207, 151, 73, 0.064, 0.058],
    [0.266, 105, 62, 0.055, 0.045],
    [0.326, 143, 70, 0.068, 0.062],
    [0.392, 91, 55, 0.078, 0.052],
    [0.475, 176, 64, 0.09, 0.085],
  ] as const

  impacts.forEach(([offset, high, low, duration, volume], index) => {
    strike(audio, start + offset, high, low, duration, volume, 'triangle')
    strike(audio, start + offset + 0.002, high * 4.6, low * 3.2, duration * 0.55, volume * 0.34, index % 2 ? 'sine' : 'square')
  })
}

function win(audio: AudioContext) {
  const start = audio.currentTime + 0.02
  const notes = [
    [0, 523],
    [0.075, 659],
    [0.15, 784],
    [0.235, 1047],
  ] as const
  notes.forEach(([offset, frequency]) => {
    strike(audio, start + offset, frequency, frequency * 1.015, 0.23, 0.072, 'sine')
    strike(audio, start + offset, frequency * 2, frequency * 2.02, 0.13, 0.022, 'triangle')
  })
  strike(audio, start + 0.34, 784, 790, 0.32, 0.052, 'sine')
  strike(audio, start + 0.34, 1047, 1055, 0.34, 0.062, 'sine')
}

export function playSicBoSound(cue: SicBoAudioCue): void {
  const audio = audioContext()
  if (!audio) return
  if (cue === 'roll') roll(audio)
  else if (cue === 'win') win(audio)
  else if (cue === 'bet') bet(audio)
  else click(audio)
}
