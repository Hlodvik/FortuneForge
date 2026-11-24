// @vitest-environment node
import { describe, expect, it } from 'vitest'
import { COSMIC_FORTUNE_EXPERIENCE_SET } from '../src/manifest'
import { PHARAOH_WAGERING, PHARAOH_WAGER_OPTIONS } from '../src/wagering'

describe('Pharaoh paid wager terms', () => {
  it('uses the shared game document for a R3 start and a R0.50 to R75 paid picker', () => {
    const rules = COSMIC_FORTUNE_EXPERIENCE_SET.rules
    expect(rules.pointValueInCents).toBe(PHARAOH_WAGERING.pointValueInCents)
    expect(rules.wagerOptions).toBe(PHARAOH_WAGER_OPTIONS)
    expect(rules.defaultWagerRand).toBe(3)
    expect(PHARAOH_WAGERING.defaultWagerPoints).toBe(12)
    expect(rules.wagerOptions).toHaveLength(150)
    expect(rules.wagerOptions[0] * rules.pointValueInCents / 100).toBe(.5)
    expect(rules.wagerOptions.at(-1)! * rules.pointValueInCents / 100).toBe(75)
    expect(rules.wagerOptions.every((points, index) => index === 0 || points - rules.wagerOptions[index - 1] === 2)).toBe(true)
  })
})
