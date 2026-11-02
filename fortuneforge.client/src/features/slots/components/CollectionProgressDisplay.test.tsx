// @vitest-environment jsdom
import { act } from 'react'
import { createRoot } from 'react-dom/client'
import { renderToStaticMarkup } from 'react-dom/server'
import { describe, expect, it, vi } from 'vitest'
import { CollectionProgressDisplay } from './CollectionProgressDisplay'

describe('CollectionProgressDisplay', () => {
  it('shows staged vault piles with responsive art and no exact countdown', () => {
    vi.stubGlobal('IS_REACT_ACT_ENVIRONMENT', true)
    vi.stubGlobal('ResizeObserver', undefined)
    const width = vi.spyOn(HTMLImageElement.prototype, 'clientWidth', 'get').mockReturnValue(44)
    const height = vi.spyOn(HTMLImageElement.prototype, 'clientHeight', 'get').mockReturnValue(44)
    const host = document.createElement('div')
    document.body.append(host)
    const root = createRoot(host)
    const candidates = '/ruby-96.webp 96w, /ruby-192.webp 192w, /ruby-320.webp 320w'
    try {
      for (const count of [0, 29]) {
        act(() => root.render(
          <CollectionProgressDisplay
            collection={{ sealId: 'sync', count, averageWagerPoints: 50, requiredCount: 30 }}
            definition={{ id: 'sync', label: 'Ruby access track', shortLabel: 'Ruby', symbol: 'SEAL_SYNC', requiredCount: 30 }}
            image="/ruby-96.webp"
            imageSrcSet={candidates}
            isImpacting={false}
            itemLabel="vault jewels"
            presentation="vault-access"
          />,
        ))
        const track = host.querySelector('[role="img"]')
        expect(track?.getAttribute('data-collection-presentation')).toBe('vault-access')
        expect(track?.hasAttribute('aria-valuenow')).toBe(false)
        expect(track?.querySelector('.slots-page__collection-count')).toBeNull()
        expect(track?.getAttribute('aria-label')).not.toContain(`${count} of 30`)
        expect(track?.querySelector('.midnight-vault__gem-pile')?.getAttribute('data-pile-stage')).toBe(count === 0 ? '0' : '4')
        const gem = track?.querySelector<HTMLImageElement>('.slots-page__vault-access-gem')
        expect(gem).not.toBeNull()
        expect(gem?.getAttribute('src')).toBe('/ruby-96.webp')
        expect(gem?.getAttribute('srcset')).toBe(candidates)
        expect(gem?.getAttribute('sizes')).toBe('44px')
      }
    } finally {
      act(() => root.unmount())
      host.remove()
      width.mockRestore()
      height.mockRestore()
      vi.unstubAllGlobals()
    }
  })

  it('keeps nearby vault counts in the same visual stage without suggesting a guaranteed next-spin trigger', () => {
    const render = (count: number) => renderToStaticMarkup(<CollectionProgressDisplay
      collection={{ sealId: 'sync', count, averageWagerPoints: 3, requiredCount: 30 }}
      definition={{ id: 'sync', label: 'Ruby', shortLabel: 'Ruby', symbol: 'SEAL_SYNC', requiredCount: 30 }}
      image="/ruby.webp" isImpacting={false} itemLabel="jewels" presentation="vault-access" />)
    expect(render(24)).toBe(render(29))
    expect(render(29)).toContain('Large pile')
    expect(render(29)).not.toContain('Vault Breach unlocked')
    expect(render(30)).toContain('Vault Breach unlocked')
  })

  it('renders Wukong collection progress as a lightweight celestial orbit', () => {
    const markup = renderToStaticMarkup(
      <CollectionProgressDisplay
        collection={{ sealId: 'sync', count: 20, averageWagerPoints: 50, requiredCount: 40 }}
        definition={{ id: 'sync', label: 'Synced reels', shortLabel: 'Sync', symbol: 'SEAL_SYNC', requiredCount: 40 }}
        image="/seal.png"
        isImpacting={false}
        itemLabel="seals"
        presentation="celestial-orbit"
      />,
    )

    expect(markup).toContain('slots-page__collection-orbit')
    expect(markup.match(/class="is-lit"/g)).toHaveLength(5)
    expect(markup).toContain('--collection-progress:50%')
    expect(markup).not.toContain('slots-page__collection-piece')
  })

  it('puts the Wukong seal effect in both the focus hint and visible tooltip', () => {
    const reward = 'Complete this orbit to launch 10 Celestial Quests that add 2–5 monkey paws to every spin.'
    const markup = renderToStaticMarkup(
      <CollectionProgressDisplay
        collection={{ sealId: 'paw', count: 17, averageWagerPoints: 50, requiredCount: 40 }}
        definition={{
          id: 'paw',
          label: 'Monkey paw rush',
          displayLabel: 'Paw Rush',
          shortLabel: 'Paws',
          symbol: 'SEAL_PAW',
          requiredCount: 40,
          rewardDescription: reward,
        }}
        image="/paw-seal.png"
        isImpacting={false}
        itemLabel="seals"
        presentation="celestial-orbit"
      />,
    )

    expect(markup).toContain('aria-label="Monkey paw rush:')
    expect(markup).toContain('17 of 40 collected')
    expect(markup.match(/add 2–5 monkey paws to every spin/g)).toHaveLength(2)
    expect(markup).toContain('aria-hidden="true">Paw Rush</strong>')
  })

  it('adds earned Pirate jewels over the reference rail without a fixed target', () => {
    const cases = [
      [0, 0],
      [1, 1],
      [3, 2],
      [4, 2],
      [7, 3],
      [10, 4],
      [15, 4],
      [21, 5],
      [24, 5],
      [30, 5],
    ] as const

    for (const [count, expectedVisibleGems] of cases) {
      const markup = renderToStaticMarkup(
        <CollectionProgressDisplay
          collection={{ sealId: 'sync', count, averageWagerPoints: 50, requiredCount: 25 }}
          definition={{ id: 'sync', label: 'Matching reel', shortLabel: 'Ruby', symbol: 'SEAL_SYNC', requiredCount: 25 }}
          image="/ruby.png"
          isImpacting
          itemLabel="gems"
          presentation="gem-hoard"
        />,
      )

      expect(markup).toContain('slots-page__pirate-gem-pile')
      expect(markup).toContain('slots-page__pirate-gem-meter')
      expect(markup).not.toContain('treasure-chest')
      expect(markup).not.toContain('25 collected')
      expect(markup).not.toContain('aria-valuenow')
      expect(markup.match(/class="is-visible"/g) ?? []).toHaveLength(expectedVisibleGems)
      expect(markup).toContain('draggable="false"')
    }
  })

  it('describes the Pirate reward without showing a numeric gem count', () => {
    const markup = renderToStaticMarkup(
      <CollectionProgressDisplay
        collection={{ sealId: 'sync', count: 9, averageWagerPoints: 50, requiredCount: 25 }}
        definition={{
          id: 'sync', label: 'Matching reel', shortLabel: 'Ruby', symbol: 'SEAL_SYNC', requiredCount: 25,
          rewardDescription: 'Matching gems build the chance of 10 free games. During every free game, one reel is copied to match the winning setup.',
        }}
        image="/ruby.png"
        isImpacting={false}
        itemLabel="gems"
        presentation="gem-hoard"
      />,
    )

    expect(markup).not.toContain('slots-page__collection-count')
    expect(markup).not.toContain('aria-valuenow')
    expect(markup).not.toContain('/25')
    expect(markup).not.toContain('title=')
    expect(markup).toContain('Collection reward')
    expect(markup).toContain('Land Ruby gems anywhere on the reels to bank them.')
    expect(markup).toContain('Matching gems build the chance of 10 free games.')
    expect(markup).toContain('During every free game, one reel is copied to match the winning setup.')
    expect(markup).toContain('tabindex="0"')
  })

  it('uses compact cabinet copy without replacing the full accessible collection label', () => {
    const markup = renderToStaticMarkup(
      <CollectionProgressDisplay
        collection={{ sealId: 'paw', count: 4, averageWagerPoints: 50, requiredCount: 25 }}
        definition={{
          id: 'paw',
          label: 'Stronger purse hauls',
          displayLabel: 'Purse Hauls',
          shortLabel: 'Orange',
          symbol: 'SEAL_PAW',
          requiredCount: 25,
        }}
        image="/orange.png"
        isImpacting={false}
        itemLabel="gems"
        presentation="gem-hoard"
      />,
    )

    expect(markup).toContain('aria-label="Stronger purse hauls:')
    expect(markup).toContain('aria-hidden="true">Purse Hauls</strong>')
  })

  it('keeps a completed gem meter visually full during the reward animation', () => {
    const markup = renderToStaticMarkup(
      <CollectionProgressDisplay
        collection={{ sealId: 'sync', count: 3, averageWagerPoints: 50, requiredCount: 25 }}
        definition={{ id: 'sync', label: 'Matching reel', shortLabel: 'Ruby', symbol: 'SEAL_SYNC', requiredCount: 25 }}
        image="/ruby.png"
        isCelebrating
        isImpacting={false}
        itemLabel="gems"
        displayCount={25}
        presentation="gem-hoard"
        showCount={false}
        statusDetail="3 gems banked"
      />,
    )

    expect(markup).toContain('slots-page__seal-collection--celebrating')
    expect(markup).toContain('slots-page__pirate-gem-pile')
    expect(markup).toContain('--collection-progress:100%')
    expect(markup.match(/class="is-visible"/g)).toHaveLength(5)
    expect(markup).toContain('3 gems banked')
    expect(markup).not.toContain('aria-valuenow')
  })
})
