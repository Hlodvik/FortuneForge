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

    expect(html).toContain('aria-label="Daily run Asteroids replay"')
    expect(html).toContain('aria-label="Asteroids deterministic replay playfield"')
    expect(html).toContain('aria-live="polite"')
    expect(html).toContain('aria-label="Touch controls"')
    expect(html).toContain('aria-label="Turn left"')
    expect(html).toContain('aria-label="Thrust"')
    expect(html).toContain('aria-label="Turn right"')
    expect(html).toContain('aria-label="Fire"')
  })
})
