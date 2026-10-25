import { describe, expect, it } from 'vitest'

// This source guard runs in Vitest's Node process, not the browser package.
const fileSystemModule = 'node:fs'
const { readFileSync } = await import(/* @vite-ignore */ fileSystemModule)
const styles: string = readFileSync(new URL('./holdem.css', import.meta.url), 'utf8')

function mediaRules(width: number): string {
  return styles.match(new RegExp(`@media \\(max-width: ${width}px\\) \\{([\\s\\S]*?)\\n\\}`))?.[1] ?? ''
}

describe('Hold’em narrow lobby layout safeguards', () => {
  it('uses full-width stake rows rather than clipping or splitting production stake names on narrow phones', () => {
    const narrow = mediaRules(380)
    expect(narrow).toMatch(/\.ff-holdem-stakes\s*\{[^}]*flex-direction:\s*column/)
    expect(narrow).toMatch(/\.ff-holdem-stakes button\s*\{[^}]*display:\s*grid/)
    expect(narrow).toMatch(/\.ff-holdem-stakes strong\s*\{[^}]*white-space:\s*nowrap/)
    expect(narrow).not.toContain('overflow-wrap: anywhere')
  })

  it('keeps the start of a vertically overflowing mobile lobby reachable at scroll position zero', () => {
    const mobile = mediaRules(600)
    expect(mobile).toMatch(/\.ff-holdem-lobby\s*\{[^}]*overflow:\s*auto[^}]*justify-content:\s*flex-start/)
  })
})
