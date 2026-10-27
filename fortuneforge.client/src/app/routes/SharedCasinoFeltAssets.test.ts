/// <reference types="node" />
import { createHash } from 'node:crypto'
import { readFileSync } from 'node:fs'
import { describe, expect, it } from 'vitest'

const file = (path: string) => new URL(`../../../../${path}`, import.meta.url)
const source = (path: string) => readFileSync(file(path), 'utf8')
const game = (name: string, path: string) => `games/${name}/client/FortuneForge.Games.${name}.Client/src/${path}`

describe('approved shared casino felt asset', () => {
  it('reuses the exact approved Hold’em image and shade instead of generating another texture', () => {
    const image = readFileSync(file('games/shared/visuals/emerald-felt-v2.webp'))
    expect(createHash('sha256').update(image).digest('hex')).toBe('0500f9a1af846d6444a10fd8e446744267aa771465ace52b3ff84bafbf8bee03')
    expect(image.equals(readFileSync(file(game('TexasHoldem', 'assets/emerald-felt-v2.webp'))))).toBe(true)
    const material = source('games/shared/visuals/casino-felt.css')
    expect(material).toContain("url('./emerald-felt-v2.webp')")
    expect(material).toContain('linear-gradient(#00150c2b, #00150c2b)')
    expect(material).toContain('#004b31 var(--ff-casino-felt-image) center / cover no-repeat')
    expect(source('fortuneforge.client/src/index.css')).toContain("@import '../../games/shared/visuals/casino-felt.css'")
  })

  it.each([
    ['Hearts', 'hearts.css'], ['CasinoWar', 'casinoWar.css'], ['TexasHoldem', 'holdem.css'],
  ])('%s owns its layout but consumes the same shared material', (name, stylesheet) => {
    const styles = source(game(name, stylesheet))
    expect(styles.trimStart()).toMatch(/^@import '\.\.\/\.\.\/\.\.\/\.\.\/shared\/visuals\/casino-felt\.css';/)
    expect(styles).toMatch(/background:\s*var\(--ff-casino-felt-material\)/)
    expect(styles).not.toContain('feTurbulence')
  })

  it('layers Andar Bahar’s transparent ornament and leather over the shared felt, including results', () => {
    const styles = source(game('AndarBahar', 'andarBahar.css'))
    expect(styles).toContain("url('./assets/palace-table-frame-v2.webp')")
    expect(styles).toContain('var(--ab-table-image) top center/100% 108% no-repeat,var(--ff-casino-felt-material)')
    expect(styles).toContain('var(--ab-table-image) top center/100% 140% no-repeat,var(--ff-casino-felt-material)')
    expect(styles).not.toContain('palace-emerald-table.webp')
    const frame = readFileSync(file(game('AndarBahar', 'assets/palace-table-frame-v2.webp')))
    expect(frame.subarray(0, 4).toString()).toBe('RIFF')
    expect(frame.includes(Buffer.from('ALPH'))).toBe(true)
  })
})
