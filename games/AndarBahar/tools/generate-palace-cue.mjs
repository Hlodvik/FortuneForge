// Original, deterministic physical-model cue. No recordings or third-party samples.
// Run with Node to regenerate the small mono PCM asset used by the game.
import { writeFile } from 'node:fs/promises'

const rate = 24000
const samples = new Float64Array(Math.ceil(rate * 1.3))
let seed = 197341
function random() { seed = (Math.imul(seed, 1664525) + 1013904223) >>> 0; return seed / 4294967296 * 2 - 1 }
function pluck(start, frequency, amplitude) {
  const delay = new Float64Array(Math.round(rate / frequency))
  for (let i = 0; i < delay.length; i++) delay[i] = random() * .65
  // Karplus–Strong string: the soft bridge reflection gives a warm, decaying pluck.
  let previous = 0
  for (let i = 0, index = Math.round(start * rate); index < samples.length; i++, index++) {
    const position = i % delay.length
    const value = delay[position]
    delay[position] = (value + previous) * .496
    previous = value
    const t = i / rate
    const envelope = Math.min(1, t / .003) * Math.exp(-t * 1.7)
    samples[index] += value * amplitude * envelope
  }
}
function handDrum(start, amplitude) {
  for (let i = 0, index = Math.round(start * rate); index < samples.length; i++, index++) {
    const t = i / rate
    const attack = Math.min(1, t / .002)
    const membrane = Math.sin(2 * Math.PI * 144 * t + .9 * (1 - Math.exp(-t * 35))) * Math.exp(-t * 18)
      + .32 * Math.sin(2 * Math.PI * 286 * t) * Math.exp(-t * 27)
      + .12 * Math.sin(2 * Math.PI * 422 * t) * Math.exp(-t * 38)
    samples[index] += amplitude * attack * (membrane + random() * .025 * Math.exp(-t * 90))
  }
}
pluck(0, 293.665, .55)
pluck(.13, 440, .5)
pluck(.27, 587.33, .45)
handDrum(.015, .12)
handDrum(.28, .07)
const peak = Math.max(...samples.map(Math.abs))
const wav = Buffer.alloc(44 + samples.length * 2)
wav.write('RIFF', 0); wav.writeUInt32LE(wav.length - 8, 4); wav.write('WAVEfmt ', 8)
wav.writeUInt32LE(16, 16); wav.writeUInt16LE(1, 20); wav.writeUInt16LE(1, 22)
wav.writeUInt32LE(rate, 24); wav.writeUInt32LE(rate * 2, 28); wav.writeUInt16LE(2, 32); wav.writeUInt16LE(16, 34)
wav.write('data', 36); wav.writeUInt32LE(samples.length * 2, 40)
for (let i = 0; i < samples.length; i++) {
  const fade = Math.min(1, (samples.length - i) / (rate * .08))
  wav.writeInt16LE(Math.round(samples[i] / Math.max(1, peak) * fade * 23000), 44 + i * 2)
}
await writeFile(new URL('../client/FortuneForge.Games.AndarBahar.Client/src/assets/palace-pluck-win.wav', import.meta.url), wav)
console.log(`Wrote original palace cue: ${wav.length} bytes, ${samples.length / rate}s`)
