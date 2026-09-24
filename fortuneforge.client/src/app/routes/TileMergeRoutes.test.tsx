import { readFileSync } from 'node:fs'
import { fileURLToPath } from 'node:url'
import { createElement } from 'react'
import { renderToStaticMarkup } from 'react-dom/server'
import { describe, expect, it } from 'vitest'
import { AppRoutes } from '../AppRoutes'

const appRoutesPath = fileURLToPath(new URL('../AppRoutes.tsx', import.meta.url))
const twentyFortyEightRoutePath = fileURLToPath(new URL('./TwentyFortyEightRoute.tsx', import.meta.url))
const dropMergeRoutePath = fileURLToPath(new URL('./DropMergeRoute.tsx', import.meta.url))

describe('tile merge routes', () => {
  it('lazy-loads both packaged arcade games through authenticated routes', () => {
    const routes = readFileSync(appRoutesPath, 'utf8')
    const twentyFortyEightRoute = readFileSync(twentyFortyEightRoutePath, 'utf8')
    const dropMergeRoute = readFileSync(dropMergeRoutePath, 'utf8')

    expect(routes).toContain("import('./routes/TwentyFortyEightRoute')")
    expect(routes).toContain("import('./routes/DropMergeRoute')")
    expect(routes).toContain("pathname === '/games/2048'")
    expect(routes).toContain("pathname === '/games/drop-merge'")
    expect(twentyFortyEightRoute).toContain("HttpTwentyFortyEightGateway('/api/games/2048', fetchWithAccountSession)")
    expect(dropMergeRoute).toContain("HttpDropMergeGateway('/api/games/drop-merge', fetchWithAccountSession)")
    expect(twentyFortyEightRoute).toContain('<InGameShell account={account} title="2048"')
    expect(dropMergeRoute).toContain('<InGameShell account={account} title="Drop Merge"')
    expect(twentyFortyEightRoute).not.toContain('GameAmbientMusic')
    expect(dropMergeRoute).not.toContain('GameAmbientMusic')
  })

  it('keeps both routes behind the authenticated loading state', () => {
    const markup = renderToStaticMarkup(createElement(AppRoutes, {
      pathname: '/games/2048',
      slotRoute: null,
      onSpinStateChange: () => undefined,
    }))

    expect(markup).toContain('Opening Fortune Forge…')
  })
})
