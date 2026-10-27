import { readFileSync } from 'node:fs'
import { describe, expect, it } from 'vitest'

const source = path => readFileSync(new URL(path, import.meta.url), 'utf8')
const styles = source('./crapsCasino.css')
const game = source('./CrapsGame.tsx')
const base = styles.slice(0, styles.indexOf('@media'))

function declarations(css, selector) {
  const escaped = selector.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')
  return css.match(new RegExp(`(?:^|\\n)\\s*${escaped}\\s*\\{([^}]+)\\}`))?.[1] ?? ''
}

function media(query) {
  const start = styles.indexOf(`@media${query}`)
  expect(start).toBeGreaterThanOrEqual(0)
  const end = styles.indexOf('@media', start + 1)
  return styles.slice(start, end < 0 ? styles.length : end)
}

describe('open-felt Craps presentation guards', () => {
  it('loads the final casino overrides after legacy viewport styles and uses the shared felt without a wood frame', () => {
    const imports = [...game.matchAll(/import\s+['"]([^'"]+\.css)['"]/g)].map(match => match[1])
    expect(imports.at(-1)).toBe('./crapsCasino.css')
    expect(imports.indexOf('./crapsViewport.css')).toBeLessThan(imports.indexOf('./crapsCasino.css'))
    expect(source('./craps.css')).toContain("@import '../../../../shared/visuals/casino-felt.css'")
    expect(declarations(base, '.ff-craps-page')).toContain('background:var(--ff-casino-felt-material)')
    const table = declarations(base, '.ff-craps-table')
    for (const rule of ['border:0', 'border-radius:0', 'background:none', 'box-shadow:none']) expect(table).toContain(rule)
    expect(declarations(base, '.ff-craps-table--default-art .ff-craps-table__felt')).toContain('background:none')
    expect(declarations(base, '.ff-craps-table--default-art .ff-craps-table__felt::before')).toContain('content:none')
    expect(game).not.toMatch(/import\s+[^\n]*craps-table-backdrop/)
    expect(styles).not.toContain('craps-felt-interior.svg')
  })

  it('keeps the phone betting cells connected and places the full-width Pass Line below them', () => {
    const phone = media('(max-width:760px)')
    expect(declarations(phone, '.ff-craps-board')).toContain('grid-template-columns:repeat(2,minmax(0,1fr))')
    for (const [selector, row] of [
      ['.ff-craps-point-row', 2], ['.ff-craps-proposition.is-field', 3], ['.ff-craps-pass-line', 5],
    ]) {
      const rule = declarations(phone, selector)
      expect(rule).toContain('grid-column:1 / -1')
      expect(rule).toContain(`grid-row:${row}`)
    }
    const seven = declarations(phone, '.ff-craps-proposition.is-any-seven')
    const craps = declarations(phone, '.ff-craps-proposition.is-any-craps')
    expect(seven).toContain('grid-column:1')
    expect(craps).toContain('grid-column:2')
    for (const rule of [seven, craps]) {
      expect(rule).toContain('grid-row:4')
      expect(rule).toContain('border-top:0')
    }
    expect(craps).toContain('border-left:0')
    expect(declarations(phone, '.ff-craps-pass-line')).toContain('border-top:0')
  })

  it('allows field content and overlays to remain visible rather than cropping them to the former artwork inset', () => {
    const board = declarations(base, '.ff-craps-board')
    for (const rule of ['position:relative', 'inset:auto', 'height:100%', 'overflow:visible']) expect(board).toContain(rule)
    expect(declarations(base, '.ff-craps-table')).toContain('overflow:visible')
    expect(declarations(base, '.ff-craps-proposition.is-field')).toContain('grid-row:3')
    expect(styles).not.toMatch(/\.ff-craps-(?:proposition(?:\.is-field)?|field-numbers)\s*\{[^}]*overflow:\s*(?:hidden|clip)/)
    expect(declarations(base, '.ff-craps-page')).toContain('overflow:auto')
    const landscape = media('(max-height:500px) and (min-width:600px)')
    expect(declarations(landscape, '.ff-craps-main')).toMatch(/min-height:\d+px/)
    const rows = declarations(landscape, '.ff-craps-board').match(/grid-template-rows:minmax\((\d+)px,1fr\) \d+px (\d+)px \d+px/)
    expect(rows).not.toBeNull()
    expect(Number(rows[1])).toBeGreaterThanOrEqual(95)
    expect(Number(rows[2])).toBeGreaterThanOrEqual(80)
  })

  it('keeps point numbers informational instead of silently presenting unsupported place bets', () => {
    const row = game.match(/<div className="ff-craps-point-row"[\s\S]+?<\/div>/)?.[0]
    expect(row).toBeDefined()
    expect(row).toContain('pointNumbers.map')
    expect(row).toContain('<span className=')
    expect(row).not.toMatch(/<button|onClick=|role="button"/)
    expect(declarations(base, '.ff-craps-point-row > span:not(.ff-craps-puck)')).not.toMatch(/cursor:\s*pointer/)
  })

  it('uses the same real navbar and shell classes in the standalone preview and production route', () => {
    const preview = source('./dev/main.tsx')
    const route = source('../../../../../fortuneforge.client/src/app/routes/CrapsRoute.tsx')
    for (const code of [preview, route]) {
      expect(code).toContain('InGameShell')
      for (const attribute of ['title="Craps"', 'theme="casino"', 'className="craps-page"', 'bodyClassName="craps-route__body"']) expect(code).toContain(attribute)
      expect(code).not.toContain('showTitle={false}')
    }
    expect(preview).not.toMatch(/<InGameShell[^>]*\baccount=/)
    expect(styles).toContain('.in-game-shell:has(.ff-craps-page) .in-game-navbar__title { display:none; }')
    expect(styles).not.toMatch(/(?:^|\n)\.in-game-navbar(?:__title)?\s*\{/)
  })
})
