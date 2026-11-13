import { requestQuickStop, startSpinAnimation } from '@fortuneforge/slot-client-core'
import { afterEach, expect, it, vi } from 'vitest'
import { stopNitroReels } from '../src/nitroPresentation'
import type { NeonCarSymbolId, NeonSymbolId } from '../src/types'

afterEach(() => vi.useRealTimers())

it.each(['pair', 'legacy pair', 'choice', 'miss', 'stop'] as const)('presents the visible Nitro reveal in order on %s', async (scenario) => {
  vi.useFakeTimers({ toFake: ['setTimeout', 'clearTimeout', 'performance', 'requestAnimationFrame', 'cancelAnimationFrame'] })
  const events: { name: string; time: number }[] = []
  const mark = (name: string) => events.push({ name, time: performance.now() })
  const initialReels: NeonCarSymbolId[][] = Array.from({ length: 5 }, () => ['2', '4', '5', '7', '2', '4', '5'])
  const trigger = { reel: scenario === 'choice' ? 3 : 1, row: 2 }
  const eligibleReel = trigger.reel + 1
  const secondNitro = scenario === 'miss' || scenario === 'legacy pair' ? undefined : { reel: eligibleReel, row: 4 }
  initialReels[trigger.reel][trigger.row] = 'NITRO'
  if (scenario !== 'miss') initialReels[eligibleReel][4] = 'NITRO'
  const expandedReel = scenario === 'choice' || scenario === 'miss' ? null : scenario === 'legacy pair' ? eligibleReel : eligibleReel + 1
  const settledReels: NeonSymbolId[][] = initialReels.map((reel, index) => index === expandedReel ? reel.map(() => 'ACE') : reel)
  const abort = new AbortController()
  const animation = startSpinAnimation<NeonSymbolId>({
    reelCount: 5, rowsPerReel: 7, reducedMotion: false, symbolIds: ['2', '4', '5', '7'],
    setReelMotion: () => {},
    displayFrame: (index, symbols) => {
      if (symbols === initialReels[index]) mark(`landed-${index}`)
      if (index === expandedReel && symbols.length === 7 && symbols.includes('ACE')) {
        mark(`wilds-${symbols.filter(symbol => symbol === 'ACE').length}`)
        expect(symbols).toEqual(settledReels[index].map((symbol, row) => row < symbols.filter(value => value === 'ACE').length ? symbol : initialReels[index][row]))
      }
    },
  })

  const presentation = stopNitroReels({
    animation, nitro: { trigger, secondNitro, eligibleReel, expandedReel, initialReels }, settledReels,
    signal: abort.signal, isQuickStopRequested: () => abort.signal.aborted,
    settleAfterLastReelMs: 110, onReelStopped: () => {},
    onPresentationChange: stage => { if (stage) mark(`${stage.phase}-${stage.targetReel}`) },
    onAnticipation: () => {
      if (scenario === 'stop') { requestQuickStop(animation); abort.abort() }
    },
  })
  await vi.runAllTimersAsync()
  await presentation

  const timeOf = (name: string) => events.find(event => event.name === name)?.time ?? -1
  const anticipation = timeOf(`anticipating-${eligibleReel}`)
  expect(timeOf(`landed-${trigger.reel}`)).toBeGreaterThan(0)
  expect(anticipation).toBeGreaterThan(timeOf(`landed-${trigger.reel}`))
  const anticipationDuration = timeOf(`landed-${eligibleReel}`) - anticipation
  if (scenario === 'stop') expect(anticipationDuration).toBeLessThan(2_000)
  else expect(anticipationDuration).toBeGreaterThanOrEqual(2_000)
  if (scenario === 'pair' || scenario === 'legacy pair') {
    const expanding = timeOf(`expanding-${expandedReel}`)
    expect(expanding).toBeGreaterThan(timeOf(`landed-${expandedReel}`))
    expect(timeOf('wilds-1')).toBeGreaterThan(expanding)
    for (let row = 2; row <= 7; row++) expect(timeOf(`wilds-${row}`) - timeOf(`wilds-${row - 1}`)).toBe(90)
  } else {
    expect(events.some(({ name }) => name.startsWith('expanding-') || name.startsWith('wilds-'))).toBe(false)
  }
})
