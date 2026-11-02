import { describe, expect, it } from 'vitest'
import { DRAGON_HOARD_SLOT_GAME } from './manifest'
import { midnightVaultGuideForModel } from './math'

describe('previously earned Midnight round guidance', () => {
  it('preserves original prices and award lengths without mutating the new-round guide', () => {
    const current = DRAGON_HOARD_SLOT_GAME.experience.symbols
    const before = JSON.stringify(current)
    const legacy = midnightVaultGuideForModel(current, 'midnight-vault-paytable-v1')
    expect(legacy.guideEntries.find(entry => entry.symbol === '3')?.secondValue).toBe('2.18×')
    expect(legacy.guideEntries.find(entry => entry.symbol === 'FREE')?.secondValue).toBe('6 Vault Breach spins')
    expect(legacy.guideNote).toContain('original payout rules')
    expect(JSON.stringify(current)).toBe(before)
    expect(midnightVaultGuideForModel(current, 'midnight-vault-paytable-v2')).toBe(current)
  })
})
