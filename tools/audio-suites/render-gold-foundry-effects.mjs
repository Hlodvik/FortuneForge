import { mkdir, writeFile } from 'node:fs/promises'

// Original Foundry Foley. Generate mono 16-bit PCM without samples or dependencies.
const rate = 24_000
const tau = 2 * Math.PI
const output = new URL('../../fortuneforge.client/public/assets/slots/gold-foundry/v1/audio/', import.meta.url)
await mkdir(output, { recursive: true })

function rise(t, start, end) {
  const x = Math.max(0, Math.min(1, (t - start) / (end - start)))
  return x * x * (3 - 2 * x)
}

async function render(name, duration, peak, shape) {
  const length = Math.round(duration * rate)
  const samples = new Float32Array(length)
  let seed = 0x5c12a41 + name.charCodeAt(0)
  let mid = 0
  let low = 0
  let maximum = 0
  for (let i = 0; i < length; i++) {
    seed ^= seed << 13
    seed ^= seed >>> 17
    seed ^= seed << 5
    const noise = (seed >>> 0) / 0x80000000 - 1
    mid += 0.19 * (noise - mid)
    low += 0.048 * (noise - low)
    samples[i] = shape(i / rate, mid - low)
    maximum = Math.max(maximum, Math.abs(samples[i]))
  }

  const wav = Buffer.alloc(44 + length * 2)
  wav.write('RIFF', 0)
  wav.writeUInt32LE(wav.length - 8, 4)
  wav.write('WAVEfmt ', 8)
  wav.writeUInt32LE(16, 16)
  wav.writeUInt16LE(1, 20)
  wav.writeUInt16LE(1, 22)
  wav.writeUInt32LE(rate, 24)
  wav.writeUInt32LE(rate * 2, 28)
  wav.writeUInt16LE(2, 32)
  wav.writeUInt16LE(16, 34)
  wav.write('data', 36)
  wav.writeUInt32LE(length * 2, 40)
  for (let i = 0; i < length; i++) {
    wav.writeInt16LE(Math.round(samples[i] * peak / maximum * 32767), 44 + i * 2)
  }
  await writeFile(new URL(name + '.wav', output), wav)
}

await render('reel-brake', 0.11, 0.53, (t, wind) =>
  rise(t, 0, 0.0015) * (1 - rise(t, 0.067, 0.106)) * (
    (0.57 * Math.sin(tau * 129 * t) + 0.22 * Math.sin(tau * 246 * t)) * Math.exp(-t * 66) +
    0.46 * wind * Math.exp(-t * 184)
  ))
