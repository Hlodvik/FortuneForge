import { readFileSync } from 'node:fs'
import { describe, expect, it } from 'vitest'

const styles = readFileSync(new URL('./mines.css', import.meta.url), 'utf8')
const shortPortrait = styles.slice(styles.indexOf('@media(max-width:760px) and (min-height:501px) and (max-height:700px)'))

describe('Mines compact portrait layout safeguards', () => {
  it('makes room for the next-mine return without shrinking the 320×568 board or its touch targets', () => {
    expect(shortPortrait).toContain('.ff-mines__board-wrap{width:min(100%,calc(100dvh - 315px),420px)}')
    // Five rows, four 6px gaps, and 4px board-bottom padding: 45px-high tiles.
    expect((568 - 315 - 4 * 6 - 4) / 5).toBeGreaterThanOrEqual(44)
    expect(shortPortrait).toContain('.ff-mines{padding:6px 10px 4px;gap:6px}')
    expect(shortPortrait).toContain('.ff-mines__playfield{padding:8px 9px;gap:6px}')
    expect(shortPortrait).toContain('.ff-mines__controls{padding:10px;gap:6px}')
    expect(shortPortrait).toContain('.ff-mines__mine-return{margin-top:3px;padding-top:3px;line-height:1.2}')
    expect(shortPortrait).not.toContain('.ff-mines__mine-return{display:none')
    expect(shortPortrait).not.toMatch(/\.ff-mines(?:__table|__controls)?\{[^}]*overflow:hidden/)
  })

  it('keeps the eyebrow on one line and retains the main action touch target', () => {
    expect(shortPortrait).toContain('.ff-mines__eyebrow{font-size:6px;white-space:nowrap}')
    expect(shortPortrait).toContain('.ff-mines__primary{min-height:49px}')
    expect(shortPortrait).toContain('@media(max-width:360px)')
    expect(shortPortrait).toContain('.ff-mines__eyebrow{font-size:5.5px;letter-spacing:.7px}')
  })

  it('reclaims control-column spacing only when a late mine return is present on short landscape screens', () => {
    const landscape = styles.slice(styles.indexOf('/* Keep the late-return disclosure inside the short landscape control column. */'))
    expect(landscape).toContain('@media(max-height:500px)')
    expect(landscape).toContain('.ff-mines__controls:has(.ff-mines__mine-return){gap:6px;padding:8px}')
    expect(landscape).toContain('.ff-mines__controls:has(.ff-mines__mine-return) .ff-mines__mine-return{margin-top:2px;padding-top:2px;line-height:1.2}')
    expect(landscape).not.toContain('.ff-mines__board-wrap')
    expect(landscape).not.toContain('.ff-mines__primary{')
  })
})
