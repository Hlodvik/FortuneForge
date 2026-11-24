import { createHash } from 'node:crypto'
import { mkdirSync, readFileSync, writeFileSync } from 'node:fs'
import { spawnSync } from 'node:child_process'
import { dirname, relative, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'

// Original, offline score and sound design. Timbres are synthesized from
// oscillators and deterministic noise; there are no recordings or samples.
const script = fileURLToPath(import.meta.url)
const repository = resolve(dirname(script), '../../../../..')
const destination = resolve(dirname(script), '../src/assets/audio')
const ffmpeg = process.env.FF_AUDIO_FFMPEG || resolve(repository,
  'output/maintenance-2026-11-02/midnight-vault-audio-refresh/reference-9L-pYOQqd4M/runtime/imageio_ffmpeg/binaries/ffmpeg-win-x86_64-v9.0.2.exe')
const rate = 44_100
const tau = Math.PI * 2
const digest = value => createHash('sha256').update(value).digest('hex')
const db = value => +(20 * Math.log10(Math.max(1e-9, value))).toFixed(3)
const frequency = midi => 440 * 2 ** ((midi - 69) / 12)
const scale = [0, 1, 4, 5, 7, 8, 11]
const degree = (index, octave = 0) => 57 + scale[((index % 7) + 7) % 7] + Math.floor(index / 7) * 12 + octave * 12

function composition(duration, loop = false) {
  const frames = Math.round(duration * rate)
  const channels = [new Float64Array(frames), new Float64Array(frames)]
  let seed = 0x70686172
  const noise = () => {
    seed ^= seed << 13; seed ^= seed >>> 17; seed ^= seed << 5
    return (seed >>> 0) / 2 ** 31 - 1
  }
  function note(at, midi, length, kind = 'oud', gain = 0.15, pan = 0) {
    const start = Math.round(at * rate)
    const count = Math.round(length * rate)
    const hz = frequency(midi)
    const left = Math.cos((pan + 1) * Math.PI / 4)
    const right = Math.sin((pan + 1) * Math.PI / 4)
    let filtered = 0
    for (let i = 0; i < count; i++) {
      const t = i / rate
      const p = tau * hz * t
      const edge = Math.max(0, Math.min(1, t / 0.005, (length - t) / 0.035))
      let value
      switch (kind) {
        case 'oud':
          value = (Math.sin(p) + 0.49 * Math.sin(2.001 * p) * Math.exp(-5 * t)
            + 0.23 * Math.sin(3.004 * p) * Math.exp(-9 * t)) * Math.exp(-4.6 * t)
          break
        case 'flute':
          value = (Math.sin(p + 0.025 * Math.sin(tau * 4.9 * t)) + 0.1 * Math.sin(2 * p))
            * Math.min(1, t / 0.08, (length - t) / 0.14) * Math.exp(-0.6 * t)
          break
        case 'bronze':
          value = (Math.sin(p) + 0.33 * Math.sin(2.71 * p) * Math.exp(-4 * t)
            + 0.11 * Math.sin(4.09 * p) * Math.exp(-9 * t)) * Math.exp(-3.7 * t)
          break
        case 'drum':
          value = (Math.sin(p + 1.8 * (1 - Math.exp(-32 * t))) * Math.exp(-15 * t)
            + noise() * 0.17 * Math.exp(-65 * t))
          break
        case 'shaker':
          filtered += 0.75 * (noise() - filtered)
          value = filtered * Math.exp(-42 * t)
          break
        case 'stone':
          value = (Math.sin(p) + Math.sin(p * 2.76) * 0.27 + noise() * 0.09) * Math.exp(-38 * t)
          break
        default: throw new Error(`Unknown instrument ${kind}`)
      }
      const index = start + i
      if (!loop && index >= frames) break
      channels[0][index % frames] += value * gain * edge * left
      channels[1][index % frames] += value * gain * edge * right
    }
  }
  function arpeggio(notes, at = 0, step = 0.12, gain = 0.22, kind = 'bronze') {
    notes.forEach((midi, i) => note(at + i * step, midi, 0.9, kind, gain, (i % 3 - 1) * 0.25))
  }
  function pcm({ peak = 0.7, rms = 0.1 } = {}) {
    const dry = channels.map(channel => channel.slice())
    for (const [seconds, gain] of [[0.047, 0.12], [0.111, 0.09], [0.193, 0.06], [0.317, 0.035]]) {
      const offset = Math.round(seconds * rate)
      for (let c = 0; c < 2; c++) for (let i = 0; i < frames; i++) {
        const from = i - offset
        if (from >= 0 || loop) channels[c][i] += dry[1 - c][(from + frames) % frames] * gain
      }
    }
    let maximum = 0, total = 0
    for (const channel of channels) {
      let mean = 0
      for (const value of channel) mean += value / frames
      for (let i = 0; i < frames; i++) {
        channel[i] = Math.tanh((channel[i] - mean) * 1.2)
        maximum = Math.max(maximum, Math.abs(channel[i]))
        total += channel[i] ** 2
      }
    }
    const level = Math.min(peak / Math.max(0.001, maximum), rms / Math.max(0.0001, Math.sqrt(total / (frames * 2))))
    const bytes = Buffer.alloc(44 + frames * 4)
    bytes.write('RIFF', 0); bytes.writeUInt32LE(bytes.length - 8, 4); bytes.write('WAVEfmt ', 8)
    bytes.writeUInt32LE(16, 16); bytes.writeUInt16LE(1, 20); bytes.writeUInt16LE(2, 22)
    bytes.writeUInt32LE(rate, 24); bytes.writeUInt32LE(rate * 4, 28)
    bytes.writeUInt16LE(4, 32); bytes.writeUInt16LE(16, 34); bytes.write('data', 36)
    bytes.writeUInt32LE(frames * 4, 40)
    const fade = Math.round(rate * (loop ? 0.003 : 0.006))
    for (let i = 0; i < frames; i++) for (let c = 0; c < 2; c++) {
      const edge = Math.min(1, i / fade, (frames - 1 - i) / fade)
      bytes.writeInt16LE(Math.round(channels[c][i] * level * edge * 32767), 44 + i * 4 + c * 2)
    }
    return bytes
  }
  return { note, arpeggio, pcm }
}

function music(bonus) {
  const beat = 60 / (bonus ? 108 : 96)
  const track = composition(64 * beat, true)
  const motif = bonus ? [0, 2, 4, 5, 4, 2, 1, 0] : [0, 2, 1, 0, 4, 3, 2, 0]
  for (let bar = 0; bar < 16; bar++) {
    const root = [0, 3, 0, 4][Math.floor(bar / 4)]
    const at = bar * 4 * beat
    track.note(at, degree(root, -2), 1.25, 'flute', 0.055, -0.3)
    track.note(at, 41, 0.35, 'drum', 0.15)
    track.note(at + 2 * beat, 50, 0.25, 'drum', 0.08, 0.12)
    track.note(at + 2.75 * beat, 57, 0.2, 'drum', 0.055, -0.12)
    for (let step = 0; step < 8; step++) {
      const when = at + step * beat / 2
      track.note(when, 86, 0.1, 'shaker', step % 2 ? 0.022 : 0.033, step % 2 ? 0.5 : -0.5)
      if (step % 2 === 0 || bonus) track.note(when, degree(root + [0, 2, 4, 2][step % 4]), 0.48, 'oud', 0.085, -0.28)
    }
    if (bar % 2 === 0) for (let i = 0; i < 4; i++) {
      const pitch = motif[(bar * 2 + i) % motif.length] + (bonus ? 7 : 0)
      track.note(at + (i * 1.5 + 0.5) * beat, degree(pitch), 0.72, 'flute', 0.067, 0.26)
    }
    if (bonus && bar % 4 === 0) track.arpeggio([69, 73, 76], at, 0.13, 0.035)
  }
  return track.pcm({ rms: bonus ? 0.12 : 0.105 })
}

const specifications = [
  ['temple-procession', 'Temple procession', 'Music', 40, true, 0.27, 'Sixteen-bar original flute and oud-inspired melody with quiet hand percussion.', () => music(false)],
  ['guardians-awaken', 'Guardians awaken', 'Music', 64 * 60 / 108, true, 0.25, 'Sixteen-bar guardian variation with brighter plucked arpeggios and bronze accents.', () => music(true)],
  ['stone-touch', 'Stone touch', 'Controls', 0.13, false, 0.5, 'Quiet carved-stone control tap.', t => t.note(0, 79, 0.11, 'stone', 0.3)],
  ['wager-step', 'Wager adjustment', 'Controls', 0.2, false, 0.46, 'Two delicate bronze ticks.', t => { t.note(0, 73, 0.12, 'stone'); t.note(0.055, 76, 0.12, 'stone') }],
  ['gate-release', 'Gate release', 'Controls', 0.55, false, 0.59, 'Low tomb-door accent with a short rising pluck.', t => { t.note(0, 38, 0.5, 'drum', 0.3); t.arpeggio([57, 61, 64], 0.03, 0.08, 0.1, 'oud') }],
  ['stone-reels', 'Stone reels turning', 'Reels', 1.5, true, 0.35, 'Restrained rhythmic stone roller loop.', t => { for (let i = 0; i < 18; i++) t.note(i / 12, 53 + i % 3, 0.06, 'stone', 0.045) }],
  ['reel-lock', 'Reel locks', 'Reels', 0.18, false, 0.56, 'Compact low stone landing accent.', t => { t.note(0, 45, 0.16, 'drum', 0.16); t.note(0.015, 74, 0.12, 'stone', 0.14) }],
  ['guardian-reels', 'Guardian reels turning', 'Reels', 1.5, true, 0.35, 'Brighter quiet stone and bronze roller loop.', t => { for (let i = 0; i < 18; i++) t.note(i / 12, 61 + i % 3, 0.07, 'stone', 0.045) }],
  ['guardian-lock', 'Guardian reel locks', 'Reels', 0.24, false, 0.54, 'Short stone detent with bronze glint.', t => { t.note(0, 48, 0.16, 'drum', 0.16); t.note(0.02, 81, 0.21, 'bronze', 0.14) }],
  ...['horus', 'thoth', 'anubis', 'wadjet'].map((guardian, index) => [
    `seal-${guardian}`, `${guardian[0].toUpperCase()}${guardian.slice(1)} seal`, 'Collections', 1.05, false, 0.53,
    `Distinct two-note bronze motif for the ${guardian} guardian seal.`, t => t.arpeggio([degree(index), degree(index + 4)], 0, 0.1, 0.16),
  ]),
  ['treasure-small', 'Small treasure', 'Wins', 1.3, false, 0.59, 'Single resolved bronze and plucked-string reward.', t => t.arpeggio([69, 73, 76], 0, 0.13, 0.18)],
  ['treasure-great', 'Great treasure', 'Wins', 1.8, false, 0.61, 'Rising bronze reward with a warm low drum.', t => { t.arpeggio([64, 69, 73, 76, 81], 0, 0.16, 0.18); t.note(0, 38, 0.6, 'drum', 0.16) }],
  ['treasure-royal', 'Royal treasure', 'Wins', 2.8, false, 0.65, 'Two-stage ascending treasure flourish.', t => { t.arpeggio([57, 64, 69, 73, 76, 81, 85], 0, 0.23, 0.17); t.note(0, 33, 0.7, 'drum', 0.2); t.note(1, 45, 0.7, 'drum', 0.14) }],
  ['scarab-trail', 'Scarab trail', 'Wins', 1.5, false, 0.61, 'Three glints resolve into one scarab reward tail.', t => t.arpeggio([76, 80, 81], 0, 0.22, 0.18)],
  ['temple-rest', 'Quiet landing', 'Wins', 0.15, false, 0.22, 'Quiet neutral stone detent without a failure melody.', t => t.note(0, 57, 0.13, 'stone', 0.08)],
  ['chamber-opens', 'Chamber opens', 'Bonus', 2.9, false, 0.64, 'Ceremonial drum lift and a single rising guardian fanfare.', t => { t.note(0, 33, 0.8, 'drum', 0.2); t.note(0.3, 45, 0.55, 'drum', 0.13); t.arpeggio([57, 61, 64, 69, 73, 76, 81], 0.25, 0.23, 0.17) }],
  ['chamber-complete', 'Chamber complete', 'Bonus', 2, false, 0.61, 'Descending bronze resolution returning to the temple.', t => t.arpeggio([81, 76, 73, 69], 0, 0.25, 0.17)],
]

function run(args, input) {
  const result = spawnSync(ffmpeg, args, { input, windowsHide: true, maxBuffer: 128 * 1024 * 1024 })
  if (result.error) throw result.error
  if (result.status !== 0) throw new Error(result.stderr.toString())
  return result.stdout
}

mkdirSync(destination, { recursive: true })
const tracks = []
for (const [id, title, group, duration, loop, volume, description, arrange] of specifications) {
  let master
  if (group === 'Music') master = arrange()
  else {
    const track = composition(duration, loop)
    arrange(track)
    master = track.pcm({ rms: group === 'Reels' ? 0.055 : group === 'Wins' || group === 'Bonus' ? 0.12 : 0.08 })
  }
  const file = `pharaoh-${id}.ogg`
  const path = resolve(destination, file)
  run(['-hide_banner', '-loglevel', 'error', '-y', '-i', 'pipe:0', '-map_metadata', '-1',
    '-c:a', 'libvorbis', '-q:a', '5', '-metadata', `comment=FortuneForge original offline synthesis. ${description}`, path], master)
  const pcm = run(['-hide_banner', '-loglevel', 'error', '-i', path, '-f', 'f32le', '-acodec', 'pcm_f32le', '-ac', '2', '-ar', String(rate), 'pipe:1'])
  let peak = 0, sum = 0
  for (let i = 0; i < pcm.length; i += 4) {
    const value = pcm.readFloatLE(i)
    if (!Number.isFinite(value)) throw new Error(`Non-finite decoded audio in ${file}`)
    peak = Math.max(peak, Math.abs(value)); sum += value * value
  }
  const decodedDuration = pcm.length / 8 / rate
  if (peak < 0.001 || peak >= 0.98 || Math.abs(decodedDuration - duration) > 0.035)
    throw new Error(`Invalid decoded bounds for ${file}: peak=${peak}; duration=${decodedDuration}`)
  const bytes = readFileSync(path)
  tracks.push({ id, title, group, file, loop, volume, description,
    durationSeconds: +decodedDuration.toFixed(6), sampleRate: rate, channels: 2,
    decodedPeakDbfs: db(peak), decodedRmsDbfs: db(Math.sqrt(sum / (pcm.length / 4))),
    decodeVerified: true, bytes: bytes.length, sha256: digest(bytes),
    source: { type: 'original-offline-synthesized-composition', pcmSha256: digest(master), pcmBytes: master.length },
    encoding: { codec: 'vorbis', container: 'ogg', quality: 5 } })
}
writeFileSync(resolve(destination, 'manifest.json'), JSON.stringify({
  version: 1, slug: 'pharaohs-ascent', title: 'Pharaoh’s Ascent', originalComposition: true,
  theme: 'Original Egyptian adventure score: synthesized oud and flute-inspired tones, hand percussion, bronze guardian chimes.',
  bpm: 96, bonusBpm: 108, meter: 4, palette: ['oud-inspired pluck', 'flute', 'hand drum', 'bronze'],
  sampleRate: rate, channels: 2,
  sourceGenerator: relative(repository, script).replaceAll('\\', '/'), generatorSha256: digest(readFileSync(script)),
  provenance: 'All cues are original offline algorithmic composition and synthesis. No external recordings, borrowed melodies, sampled instruments, network services or generative audio services were used.',
  tracks,
}, null, 2) + '\n')
console.log(`Generated and decoded ${tracks.length} original Pharaoh’s Ascent cues; ${(tracks.reduce((sum, track) => sum + track.bytes, 0) / 1024 / 1024).toFixed(2)} MiB.`)
