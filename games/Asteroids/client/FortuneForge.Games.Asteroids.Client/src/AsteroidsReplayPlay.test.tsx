import { renderToStaticMarkup } from 'react-dom/server'
import { describe, expect, it } from 'vitest'
import { AsteroidsReplayPlay } from './AsteroidsReplayPlay'

describe('AsteroidsReplayPlay', () => {
  it('statically exposes a labeled canvas, live status, and accessible touch controls', () => {
    const html = renderToStaticMarkup(<AsteroidsReplayPlay
      runId="asteroids_0123456789abcdef"
      seedHex="000000000000002a"
      modeLabel="Daily run"
      onComplete={() => { throw new Error('Static rendering must not complete a run.') }} />)

    expect(html).toContain('aria-label="Daily run Asteroid Blaster run"')
    expect(html).toContain('aria-label="Asteroid Blaster playfield"')
    expect(html).toContain('Asteroid Blaster')
    expect(html).toContain('aria-live="polite"')
    expect(html).toContain('aria-label="Touch controls"')
    expect(html).toContain('aria-label="Direction control"')
    expect(html).toContain('aria-label="Fire"')
    expect(html).not.toContain('Server-seeded local simulation')
    expect(html).not.toContain('Frame')
  })
})
