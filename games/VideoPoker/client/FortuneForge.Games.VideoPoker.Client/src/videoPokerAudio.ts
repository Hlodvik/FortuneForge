type AudioWindow = Window & typeof globalThis & { webkitAudioContext?: typeof AudioContext }

let context: AudioContext | null = null

function audioContext(): AudioContext | null {
  if (typeof window === 'undefined') return null
  const AudioContextType = window.AudioContext ?? (window as AudioWindow).webkitAudioContext
  if (!AudioContextType) return null
  context ??= new AudioContextType()
  if (context.state === 'suspended') void context.resume()
  return context
}

function tone(frequency: number, start: number, duration: number, volume: number, type: OscillatorType) {
  const audio = audioContext()
  if (!audio) return
  const oscillator = audio.createOscillator()
  const gain = audio.createGain()
  oscillator.type = type
  oscillator.frequency.setValueAtTime(frequency, start)
  gain.gain.setValueAtTime(0.0001, start)
  gain.gain.exponentialRampToValueAtTime(volume, start + 0.008)
  gain.gain.exponentialRampToValueAtTime(0.0001, start + duration)
  oscillator.connect(gain).connect(audio.destination)
  oscillator.start(start)
  oscillator.stop(start + duration + 0.01)
}

export function playVideoPokerClick() {
  const audio = audioContext()
  if (!audio) return
  tone(185, audio.currentTime, 0.045, 0.035, 'triangle')
}

export function playVideoPokerWin(payout: number) {
  const audio = audioContext()
  if (!audio) return
  const start = audio.currentTime + 0.02
  const notes = payout >= 100 ? [523, 659, 784, 1047] : [523, 659, 784]
  notes.forEach((frequency, index) => tone(frequency, start + index * 0.085, 0.16, 0.055, 'sine'))
}
