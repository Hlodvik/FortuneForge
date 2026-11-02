import { readFileSync } from 'node:fs'
import { describe, expect, it } from 'vitest'
import { DRAGON_HOARD_SLOT_GAME } from './manifest'

const css = readFileSync(new URL('./midnightVaultCelebration.css', import.meta.url), 'utf8')

describe('Midnight Vault celebration presentation', () => {
  it('selects original geometric jewel glints instead of the legacy dragon effect', () => {
    const effect = DRAGON_HOARD_SLOT_GAME.experience.cabinet.celebrationEffect
    expect(effect).toBe('vault-jewel-glint')
    expect(css).toContain(`[data-celebration='${effect}']::before`)
    expect(css).toContain("content: '✦ ◇ ✧ ◇ ✦'")
    expect(css).toContain('@keyframes vault-jewel-glint')
    expect(css).not.toMatch(/🐉|🔥|url\(/)
  })

  it('uses navy and brass for the retained feature without changing its identity', () => {
    expect(DRAGON_HOARD_SLOT_GAME.experience.features.specialRound?.id).toBe('dragon-ember-siege')
    expect(css).toContain("[data-slot-celebration='vault-jewel-glint'][data-special-round='dragon-ember-siege']")
    expect(css).toContain('--special-round-accent: #dec18b')
    expect(css).toContain('--special-round-deep: #173149')
    expect(css).toContain('--special-round-ink: #071321')
  })

  it('suppresses the decorative effect for reduced motion', () => {
    const mediaStart = css.search(/@media\s*\(prefers-reduced-motion:\s*reduce\)\s*\{/)
    expect(mediaStart).toBeGreaterThanOrEqual(0)
    const openingBrace = css.indexOf('{', mediaStart)
    let depth = 1
    let closingBrace = openingBrace + 1
    for (; closingBrace < css.length && depth > 0; closingBrace += 1) {
      if (css[closingBrace] === '{') depth += 1
      else if (css[closingBrace] === '}') depth -= 1
    }
    expect(depth).toBe(0)
    const reducedMotionRules = css.slice(openingBrace + 1, closingBrace - 1)
    const atmosphereRule = reducedMotionRules.match(/\.slots-page__result-atmosphere\[data-celebration='vault-jewel-glint'\]\s*\{([^}]*)\}/)?.[1]
    expect(atmosphereRule).toMatch(/display:\s*none;/)
    expect(atmosphereRule).toMatch(/animation:\s*none;/)
    expect(reducedMotionRules).toMatch(/\.slot-symbol--winner\s+\.slot-symbol__image\s*\{[^}]*animation:\s*none;/)
  })

  it('keeps dragon terminology out of the game-facing labels and outcome copy', () => {
    const game = DRAGON_HOARD_SLOT_GAME
    const { cabinet, symbols, features, outcomeNarrative, help } = game.experience
    const labels = [
      game.catalog.title, game.catalog.shortTitle, game.catalog.description,
      cabinet.accessibleName, cabinet.title, cabinet.subtitle,
      ...Object.values(symbols.definitions).map(symbol => symbol?.label),
      features.collections?.ariaLabel, features.collections?.itemLabel,
      ...(features.collections?.entries.flatMap(entry => [entry.label, entry.shortLabel, entry.rewardDescription]) ?? []),
      features.specialRound?.title, features.specialRound?.earnLabel, features.specialRound?.earnHint,
      ...Object.values(features.specialRound?.activeModes ?? {}),
      ...Object.values(outcomeNarrative ?? {}),
      ...(help.extraSections?.flatMap(section => [section.title, section.body]) ?? []),
    ].filter(Boolean).join(' ')
    expect(labels).toContain('Midnight Vault')
    expect(labels).not.toMatch(/\b(?:dragon|ember|siege|knight|firelit|flight)s?\b/i)
  })
})
