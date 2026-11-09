// @vitest-environment jsdom
import { act } from 'react'
import { createRoot } from 'react-dom/client'
import { describe, expect, it, vi } from 'vitest'
import { NEON_NIGHTS_SOUNDS } from '../src/audio'
import type { SlotSoundSet } from '@fortuneforge/slot-client-core'
import { useSlotAudio } from '@fortuneforge/slot-client-core'

function mountedAudio(run: (controls: () => ReturnType<typeof useSlotAudio>, instances: FakeAudio[], render: (soundSet: SlotSoundSet) => void) => void) {
  const instances: FakeAudio[] = []
  vi.stubGlobal('IS_REACT_ACT_ENVIRONMENT', true)
  vi.stubGlobal('Audio', class extends FakeAudio {
    constructor(source: string) { super(source); instances.push(this) }
  })
  window.localStorage.clear()
  const host = document.createElement('div')
  document.body.append(host)
  const root = createRoot(host)
  let controls: ReturnType<typeof useSlotAudio> | undefined
  function Harness({ soundSet }: { soundSet: SlotSoundSet }) { controls = useSlotAudio(soundSet); return null }
  const current = () => { if (!controls) throw new Error('Not mounted'); return controls }
  const render = (soundSet: SlotSoundSet) => act(() => root.render(<Harness soundSet={soundSet} />))
  try {
    render(NEON_NIGHTS_SOUNDS)
    expect(instances).toHaveLength(0)
    run(current, instances, render)
  } finally {
    act(() => root.unmount())
    expect(instances.every(audio => audio.paused)).toBe(true)
    host.remove()
    window.localStorage.clear()
    vi.unstubAllGlobals()
  }
}

class FakeAudio {
  src: string
  loop = false
  volume = 1
  currentTime = 0
  paused = true
  play = vi.fn(() => { this.paused = false; return Promise.resolve() })
  pause = vi.fn(() => { this.paused = true })
  addEventListener = vi.fn()
  constructor(source: string) { this.src = source }
}

describe('theme-suite audio lifecycle', () => {
  it('keeps one music bed, retains concurrent reel texture, and obeys results-only/mute', () => mountedAudio((current, instances) => {
    act(() => { current().startLoop('ambience'); current().startLoop('reel-spin') })
    const [base, reels] = instances
    act(() => current().startLoop('special-ambience'))
    const bonus = instances[2]
    expect(base.paused).toBe(true)
    expect(reels.paused).toBe(false)
    expect(bonus.paused).toBe(false)
    act(() => current().startLoop('special-ambience'))
    expect(instances).toHaveLength(3)
    act(() => current().startLoop('ambience'))
    expect(bonus.paused).toBe(true)
    expect(instances[3].paused).toBe(false)
    act(() => current().setVolume(20))
    expect(instances[3].volume).toBeCloseTo(.62 * .2)
    act(() => current().toggleResultsOnly())
    expect(instances.every(audio => audio.paused)).toBe(true)
    act(() => { current().startLoop('ambience'); current().playCue('collect-sync'); current().playCue('low-win') })
    expect(instances).toHaveLength(5)
    expect(instances[4].src).toContain('win-nice-v1.ogg')
    act(() => current().toggleMuted())
    expect(instances[4].paused).toBe(true)
  }))

  it('bounds collection voices to one per track while allowing the four distinct accents', () => mountedAudio((current, instances) => {
    act(() => {
      current().playCue('collect-sync'); current().playCue('collect-sync')
      current().playCue('collect-rows'); current().playCue('collect-paw'); current().playCue('collect-rand')
    })
    expect(instances).toHaveLength(5)
    expect(instances[0].paused).toBe(true)
    expect(instances.filter(audio => !audio.paused)).toHaveLength(4)
    act(() => current().stopResultCues())
    expect(instances.filter(audio => !audio.paused)).toHaveLength(4)
  }))

  it('deduplicates the Neon stinger and stops it when requested', () => mountedAudio((current, instances) => {
    act(() => current().playSequence(['low-win', 'low-win']))
    expect(instances).toHaveLength(1)
    act(() => current().stopResultCues())
    expect(instances[0].paused).toBe(true)
  }))
})
