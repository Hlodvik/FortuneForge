import { mkdirSync, writeFileSync } from 'node:fs'

// Original additive synthesis: no sampled music or third-party recordings.
const rate = 32_000
const root = new URL('../src/assets/audio/sunstone-eclipse/', import.meta.url)
mkdirSync(root, { recursive: true })

function render(file, duration, events, { loop = false, volume = 0.45 } = {}) {
  const length = Math.round(duration * rate)
  const samples = new Float64Array(length)
  let seed = 317_029
  const noise = () => { seed ^= seed << 13; seed ^= seed >>> 17; seed ^= seed << 5; return (seed >>> 0) / 2 ** 31 - 1 }
  for (const { start, duration: noteDuration, frequency, instrument = 'stone', gain = 1 } of events) {
    const offset = Math.round(start * rate)
    const noteLength = Math.min(Math.round(noteDuration * rate), length - offset)
    for (let i = 0; i < noteLength; i++) {
      const t = i / rate
      const position = t / noteDuration
      const attack = Math.min(1, t / 0.008)
      const release = Math.min(1, (noteDuration - t) / 0.07)
      let value
      if (instrument === 'drum') {
        const phase = 2 * Math.PI * (frequency * t + frequency * 0.018 * (1 - Math.exp(-t / 0.018)))
        value = (Math.sin(phase) * 0.85 + noise() * 0.15) * Math.exp(-position * 6)
      } else if (instrument === 'flute') {
        value = (Math.sin(2 * Math.PI * frequency * t + Math.sin(2 * Math.PI * 4 * t) * 0.018)
          + Math.sin(2 * Math.PI * frequency * 2 * t) * 0.12 + noise() * 0.025)
          * Math.sin(Math.PI * position) ** 0.7
      } else if (instrument === 'breath') {
        value = noise() * 0.25 * Math.sin(Math.PI * position) ** 2
      } else {
        value = (Math.sin(2 * Math.PI * frequency * t)
          + Math.sin(2 * Math.PI * frequency * 2.756 * t) * 0.32
          + Math.sin(2 * Math.PI * frequency * 5.404 * t) * 0.1) * Math.exp(-position * 7)
      }
      samples[offset + i] += value * attack * release * gain
    }
  }
  const fade = loop ? Math.round(rate * 0.05) : 0
  const data = Buffer.alloc(length * 2)
  for (let i = 0; i < length; i++) {
    const edge = fade ? Math.min(1, i / fade, (length - 1 - i) / fade) : 1
    data.writeInt16LE(Math.round(Math.tanh(samples[i] * volume) * edge * 32_000), i * 2)
  }
  const header = Buffer.alloc(44)
  header.write('RIFF', 0); header.writeUInt32LE(36 + data.length, 4); header.write('WAVEfmt ', 8)
  header.writeUInt32LE(16, 16); header.writeUInt16LE(1, 20); header.writeUInt16LE(1, 22)
  header.writeUInt32LE(rate, 24); header.writeUInt32LE(rate * 2, 28)
  header.writeUInt16LE(2, 32); header.writeUInt16LE(16, 34)
  header.write('data', 36); header.writeUInt32LE(data.length, 40)
  writeFileSync(new URL(file, root), Buffer.concat([header, data]))
}

const note = (start, frequency, duration = 0.4, instrument = 'stone', gain = 1) => ({ start, frequency, duration, instrument, gain })
const chord = (frequencies, { step = 0.16, instrument = 'stone', duration = 0.65 } = {}) =>
  frequencies.map((frequency, index) => note(index * step, frequency, duration, instrument))

render('temple-dawn.wav', 12, [
  ...Array.from({ length: 8 }, (_, i) => note(i * 1.5, i % 2 ? 73.42 : 55, 0.8, 'drum', 0.35)),
  ...[220, 261.63, 329.63, 293.66, 220, 196].map((frequency, i) => note(i * 2 + 0.4, frequency, 1.5, 'flute', 0.28)),
], { loop: true, volume: 0.4 })
render('eclipse-procession.wav', 8, [
  ...Array.from({ length: 16 }, (_, i) => note(i * 0.5, i % 4 ? 82.41 : 55, 0.4, 'drum', 0.22)),
  ...[329.63, 392, 440, 392].map((frequency, i) => note(i * 2 + 0.3, frequency, 1.6, 'flute', 0.25)),
], { loop: true, volume: 0.36 })
render('stone-touch.wav', 0.12, [note(0, 880, 0.1, 'stone', 0.55)])
render('jade-step.wav', 0.18, chord([440, 659.25], { step: 0.045, duration: 0.12 }))
render('wheel-release.wav', 0.65, [note(0, 82.41, 0.6, 'drum', 0.75), ...chord([220, 293.66, 440], { step: 0.08, duration: 0.35 })])
render('calendar-turn.wav', 1, Array.from({ length: 12 }, (_, i) => note(i / 12, 180 + (i % 3) * 40, 0.08, 'stone', 0.22)), { loop: true })
render('ring-lock.wav', 0.45, [note(0, 110, 0.35, 'drum', 0.5), note(0.035, 659.25, 0.4, 'stone', 0.6)])
render('sun-glint.wav', 0.9, chord([329.63, 440, 659.25], { step: 0.12 }))
render('guardian-awakens.wav', 1.45, [...chord([220, 329.63, 440, 659.25], { step: 0.17, duration: 0.9 }), note(0.15, 55, 0.7, 'drum', 0.65)])
render('fifth-sun.wav', 2.6, [...chord([220, 293.66, 329.63, 440, 587.33, 659.25], { step: 0.22, duration: 1.25 }), note(0, 55, 0.8, 'drum', 0.8), note(1.35, 110, 0.7, 'drum', 0.65)])
render('eclipse-opens.wav', 2.2, [note(0, 55, 1.1, 'drum', 0.8), note(0.05, 110, 1.5, 'flute', 0.45), ...chord([220, 293.66, 440, 659.25], { step: 0.28, duration: 1 })])
render('sunrise-returns.wav', 1.7, chord([659.25, 440, 329.63, 220], { step: 0.22, duration: 0.9 }))
render('temple-breath.wav', 0.35, [note(0, 110, 0.3, 'breath', 0.4)], { volume: 0.25 })
console.log('Generated 13 original Sunstone Eclipse WAV cues.')
