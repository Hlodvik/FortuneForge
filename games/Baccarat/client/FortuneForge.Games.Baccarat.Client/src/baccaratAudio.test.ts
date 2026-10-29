import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import { playBaccaratChip, readBaccaratMuted, stopBaccaratAudio, writeBaccaratMuted } from './baccaratAudio'

const playback: { src: string; volume: number; paused: boolean }[] = []
const values = new Map<string, string>()

class FakeAudio {
  volume = 1
  currentTime = 0
  constructor(readonly src: string) {}
  addEventListener() {}
  play() { playback.push({ src: this.src, volume: this.volume, paused: false }); return Promise.resolve() }
  pause() { const entry = [...playback].reverse().find(item => item.src === this.src && !item.paused); if (entry) entry.paused = true }
}

beforeEach(() => {
  playback.length = 0
  values.clear()
  vi.stubGlobal('Audio', FakeAudio)
  vi.stubGlobal('localStorage', { getItem: (key: string) => values.get(key) ?? null, setItem: (key: string, value: string) => values.set(key, value) })
})
afterEach(() => { stopBaccaratAudio(); vi.unstubAllGlobals() })

it('plays the established chip-lay sound on a placed chip and stops it on mute', () => {
  expect(readBaccaratMuted()).toBe(false)
  playBaccaratChip(false)
  expect(playback).toHaveLength(1)
  expect(playback[0].src).toContain('chip-lay-1.ogg')
  expect(playback[0].volume).toBeLessThan(1)

  writeBaccaratMuted(true)
  expect(readBaccaratMuted()).toBe(true)
  expect(playback[0].paused).toBe(true)
  playBaccaratChip(readBaccaratMuted())
  expect(playback).toHaveLength(1)
})
