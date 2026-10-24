import { readFileSync } from 'node:fs'
import { describe, expect, it } from 'vitest'

describe('Dragon Tiger host-owned card face contract', () => {
  it('scopes every fallback face rule away from the injected canonical card and adds no inner frame', () => {
    const styles = readSource('./standardCardFaces.css')
    const rules = [...styles.replace(/\/\*[\s\S]*?\*\//g, '').matchAll(/([^{}]+)\{([^{}]*)\}/g)]
    expect(rules.length).toBeGreaterThan(0)
    for (const [, selector] of rules) expect(selector).toContain('.ff-playing-card--standalone')
    const courtRule = rules.find(([, selector]) => selector.trim().endsWith('.ff-playing-card__center--court'))
    expect(courtRule).toBeDefined()
    expect(courtRule[2]).not.toMatch(/(?:border(?:-radius)?|background|box-shadow)\s*:/)
    expect(readSource('./dragonTiger.css')).toContain('.ff-dt__card-face-host > .ff-playing-card { --ff-card-width:100cqw; }')
  })

  it('uses the canonical shared renderer in the local preview rather than the fallback', () => {
    const preview = readSource('./dev/main.tsx')
    expect(preview).toContain("import { PlayingCard } from '../../../../../../fortuneforge.client/src/games/cards/shared/PlayingCard'")
    expect(preview).toContain("import '../../../../../../fortuneforge.client/src/games/cards/shared/playingCards.css'")
    expect(preview).toContain('renderCardFace={renderCardFace}')
    expect(preview).toContain('rank: cardRanks[card.rank]')
  })
})

function readSource(path) {
  return readFileSync(new URL(path, import.meta.url), 'utf8')
}
