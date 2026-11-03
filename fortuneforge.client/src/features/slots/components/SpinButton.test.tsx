import { renderToStaticMarkup } from 'react-dom/server'
import { describe, expect, it } from 'vitest'
import { SpinButton } from './SpinButton'

describe('SpinButton', () => {
  it('states the next action without presenting a one-shot spin as a toggle', () => {
    const markup = renderToStaticMarkup(<SpinButton onSpin={() => undefined} />)

    expect(markup).toContain('aria-label="Spin the reels"')
    expect(markup).toContain('>Spin</strong>')
    expect(markup).not.toContain('aria-pressed')
  })

  it('keeps the stop action explicit while the reels are active', () => {
    const markup = renderToStaticMarkup(<SpinButton isSpinning onSpin={() => undefined} />)

    expect(markup).toContain('aria-label="Stop the spin"')
    expect(markup).toContain('>Stop</strong>')
  })

  it('renders the icon supplied by the cabinet', () => {
    const markup = renderToStaticMarkup(
      <SpinButton iconSrc="/example-spin-icon.png" onSpin={() => undefined} />,
    )

    expect(markup).toContain('spin-button--custom-icon')
    expect(markup).toContain('href="/example-spin-icon.png"')
    expect(markup).not.toContain('spin-button__arrow-icon')
  })
})
