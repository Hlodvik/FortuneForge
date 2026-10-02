import { renderToStaticMarkup } from 'react-dom/server'
import { describe, expect, it, vi } from 'vitest'
import type { SicBoGateway } from './contracts'
import { Die, SicBoGame } from './SicBoGame'

function gateway(): SicBoGateway {
  return { getStatus: vi.fn(), getRound: vi.fn(), createRound: vi.fn() }
}

function coordinates(markup: string): string[] {
  return [...markup.matchAll(/<i\b[^>]*style="([^"]+)"/g)].map(([, style]) => {
    const row = style!.match(/grid-row:(\d+)/)?.[1]
    const column = style!.match(/grid-column:(\d+)/)?.[1]
    return `${row},${column}`
  })
}

describe('Sic Bo server rendering', () => {
  it('renders the complete inert board without a browser, a request or a fabricated round', () => {
    const server = gateway()
    const markup = renderToStaticMarkup(<SicBoGame gateway={server} playerId="alex:account" />)

    expect(markup).toContain('<h1>Sic Bo</h1>')
    expect(markup.match(/data-target="/g)).toHaveLength(52)
    expect(markup.match(/aria-label="Add [^"]+ bet"/g)).toHaveLength(52)
    expect(markup).toContain('aria-label="Dice tray"')
    expect(markup).toContain('disabled="">Roll dice</button>')
    expect(markup).not.toContain('Winning bet')
    expect(markup).not.toContain('Losing bet')
    expect(server.getStatus).not.toHaveBeenCalled()
    expect(server.getRound).not.toHaveBeenCalled()
    expect(server.createRound).not.toHaveBeenCalled()
  })

  it('allows the host to supply the title and keeps the standalone title by default', () => {
    const markup = renderToStaticMarkup(<SicBoGame gateway={gateway()} showTitle={false} />)
    expect(markup).not.toContain('<h1>')
    expect(markup).toContain('aria-label="Table details"')
    expect(markup).toContain('aria-label="Sic Bo betting table"')
  })

  it('server-renders currency choices and never exposes the internal service mode', () => {
    const markup = renderToStaticMarkup(<SicBoGame gateway={gateway()} currencySymbol="$" />)
    expect(markup).toMatch(/\$0[.,]00/)
    expect(markup).not.toMatch(/R0[.,]00/)
    expect(markup).not.toContain('three-dice-sic-bo')
  })
})

describe('recognizable nine-cell dice faces', () => {
  it.each([
    [1, ['2,2']],
    [2, ['1,1', '3,3']],
    [3, ['1,1', '2,2', '3,3']],
    [4, ['1,1', '1,3', '3,1', '3,3']],
    [5, ['1,1', '1,3', '2,2', '3,1', '3,3']],
    [6, ['1,1', '1,3', '2,1', '2,3', '3,1', '3,3']],
  ] as const)('draws face %s with its conventional pip count and positions', (face, expected) => {
    const markup = renderToStaticMarkup(<Die face={face} />)
    const pips = coordinates(markup)
    expect(pips).toEqual(expected)
    expect(new Set(pips).size).toBe(face)
    expect(markup).toContain('aria-hidden="true"')
  })

  it('keeps a pending die neutral without displaying a made-up pip or private result', () => {
    const markup = renderToStaticMarkup(<Die face={null} rolling />)
    expect(coordinates(markup)).toEqual([])
    expect(markup).toContain('is-rolling')
    expect(markup).not.toMatch(/<i\b/)
  })

  it('preserves the same face geometry at the smaller board/history size', () => {
    expect(coordinates(renderToStaticMarkup(<Die face={6} small />)))
      .toEqual(coordinates(renderToStaticMarkup(<Die face={6} />)))
    expect(renderToStaticMarkup(<Die face={6} small />)).toContain('is-small')
  })
})
