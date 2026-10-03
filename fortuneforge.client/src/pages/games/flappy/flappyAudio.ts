type FlappySound = 'flap' | 'score' | 'collision'

let context: AudioContext | null = null

export function playFlappySound(sound: FlappySound): void {
  if (typeof window === 'undefined') return
  const AudioContextConstructor = window.AudioContext
  if (!AudioContextConstructor) return

  context ??= new AudioContextConstructor()
  if (context.state === 'suspended') void context.resume()

  const now = context.currentTime
  if (sound === 'flap') {
    tone(now, 520, 820, .07, 'triangle', .055)
    return
  }
  if (sound === 'score') {
    tone(now, 880, 1040, .08, 'sine', .065)
    tone(now + .065, 1180, 1420, .1, 'sine', .06)
    return
  }
  tone(now, 180, 74, .16, 'sawtooth', .08)
  tone(now + .025, 95, 48, .2, 'square', .035)
}

function tone(
  startsAt: number,
  from: number,
  to: number,
  duration: number,
  shape: OscillatorType,
  volume: number,
): void {
  if (!context) return
  const oscillator = context.createOscillator()
  const gain = context.createGain()
  oscillator.type = shape
  oscillator.frequency.setValueAtTime(from, startsAt)
  oscillator.frequency.exponentialRampToValueAtTime(to, startsAt + duration)
  gain.gain.setValueAtTime(.0001, startsAt)
  gain.gain.exponentialRampToValueAtTime(volume, startsAt + .012)
  gain.gain.exponentialRampToValueAtTime(.0001, startsAt + duration)
  oscillator.connect(gain).connect(context.destination)
  oscillator.start(startsAt)
  oscillator.stop(startsAt + duration + .01)
}
