import { describe, expect, it } from 'vitest'
import { betLabel, pocketColor, returnCopy } from './rouletteHelpers'

describe('roulette helpers', () => {
  it('maps single-zero pocket colors', () => { expect(pocketColor(0)).toBe('green'); expect(pocketColor(1)).toBe('red'); expect(pocketColor(2)).toBe('black') })
  it('labels inside, column, dozen, and outside bets', () => { expect(betLabel('straight', 17)).toBe('Number 17'); expect(betLabel('split')).toBe('Split'); expect(betLabel('column', 2)).toBe('Column 2'); expect(betLabel('dozen', 3)).toBe('Dozen 3'); expect(betLabel('low')).toBe('1–18'); expect(betLabel('high')).toBe('19–36') })
  it('describes win returns', () => { expect(returnCopy(10, true, 'straight')).toContain('35:1'); expect(returnCopy(10, false, 'red')).toBe('No return') })
})
