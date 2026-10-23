// @vitest-environment jsdom
import { render } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import { HorseBiomeScenery } from './HorseBiomeScenery'
import { HorseBiomeObstacleArt, isHorseBiomeObstacleKind } from './HorseBiomeObstacleArt'
import { SkeletonHoundArt } from './SkeletonHoundArt'
import { biomeSectionForTick, draftMultiBiomeRulesVersion, type ObstacleKind } from './horseFlightSimulation'

const mainTicks = [0, 4_250, 7_750, 11_250, 15_000, 18_250, 21_750]
const boundaryTicks = [3_875, 7_375, 10_875, 14_625, 17_875, 21_375]
const customKinds: readonly ObstacleKind[] = [
  'market-cart', 'hanging-sign', 'thorn-bramble', 'spike-barricade', 'cactus', 'storm-cloud',
]

describe('Horse Flight biome art', () => {
  it('keeps decorative mountain pines stable in world space without repeating each viewport', () => {
    const section = { phase: 'main', biome: 'mountain' } as const
    const treeLayout = (container: HTMLElement) => Array.from(container.querySelectorAll('[data-scenery-decoration="pine"]'))
      .map(tree => `${tree.getAttribute('data-world-cell')}:${tree.getAttribute('transform')}`)
    const { container, rerender } = render(<svg><HorseBiomeScenery section={section} layer="foreground" distance={0} /></svg>)
    const openingTrees = treeLayout(container)
    expect(openingTrees.length).toBeGreaterThan(1)
    expect(container.querySelector('[data-scenery-layer="foreground"]')?.querySelector('[data-obstacle-kind]')).toBeNull()

    rerender(<svg><HorseBiomeScenery section={section} layer="foreground" distance={0} /></svg>)
    expect(treeLayout(container)).toEqual(openingTrees)

    rerender(<svg><HorseBiomeScenery section={section} layer="foreground" distance={960 / .34} /></svg>)
    expect(treeLayout(container)).not.toEqual(openingTrees)
    expect(treeLayout(container).length).toBeGreaterThan(1)
  })

  it('has all three scenery layers for each main biome', () => {
    for (const tick of mainTicks) {
      const section = biomeSectionForTick(tick, draftMultiBiomeRulesVersion)
      expect(section.phase).toBe('main')
      const { container, unmount } = render(<svg>
        <HorseBiomeScenery section={section} layer="background" distance={tick * 6} tick={tick} rulesVersion={draftMultiBiomeRulesVersion} />
        <HorseBiomeScenery section={section} layer="midground" distance={tick * 6} tick={tick} rulesVersion={draftMultiBiomeRulesVersion} />
        <HorseBiomeScenery section={section} layer="foreground" distance={tick * 6} tick={tick} rulesVersion={draftMultiBiomeRulesVersion} />
      </svg>)
      expect(container.querySelectorAll('[data-scenery-layer]')).toHaveLength(3)
      expect(container.querySelectorAll(`[data-biome="${section.biome}"]`)).toHaveLength(3)
      for (const layer of ['background', 'midground', 'foreground']) {
        expect(container.querySelector(`[data-scenery-layer="${layer}"]`)?.childElementCount).toBeGreaterThan(0)
      }
      unmount()
    }
  })

  it('draws distinct boundary sections for every transition', () => {
    for (const tick of boundaryTicks) {
      const section = biomeSectionForTick(tick, draftMultiBiomeRulesVersion)
      expect(section.phase).toBe('transition')
      const { container, unmount } = render(<svg>
        <HorseBiomeScenery section={section} layer="midground" distance={tick * 6} tick={tick} rulesVersion={draftMultiBiomeRulesVersion} />
      </svg>)
      expect(container.querySelector('[data-transition]')?.getAttribute('data-transition')).toBe(`${section.from}-to-${section.to}`)
      expect(container.querySelector('[data-transition]')?.children.length).toBeGreaterThan(2)
      unmount()
    }
  })

  it('keeps the legacy haunted raster layers but omits unreviewed foreground trees in new runs', () => {
    const section = biomeSectionForTick(15_000)
    const { container } = render(<svg>
      <HorseBiomeScenery section={section} layer="background" tick={15_000} />
      <HorseBiomeScenery section={section} layer="midground" tick={15_000} />
      <HorseBiomeScenery section={section} layer="foreground" tick={15_000} />
    </svg>)
    expect(container.querySelectorAll('[data-biome="haunted"] image').length).toBeGreaterThan(0)
    expect(container.querySelector('[data-scenery-layer="foreground"] path')).toBeNull()
  })

  it('renders every new hazard with transparent vector art', () => {
    for (const kind of customKinds) {
      expect(isHorseBiomeObstacleKind(kind)).toBe(true)
      if (!isHorseBiomeObstacleKind(kind)) continue
      const { container, unmount } = render(<svg><HorseBiomeObstacleArt kind={kind} width={76} height={52} /></svg>)
      const art = container.querySelector('.ff-horse-flight__biome-obstacle-art')
      expect(art?.children.length).toBeGreaterThan(0)
      expect(art?.querySelector('image')).toBeNull()
      unmount()
    }
  })

  it('animates the undead hound without the old checkerboard sheet', () => {
    const { container, rerender } = render(<svg><SkeletonHoundArt frame={2} /></svg>)
    const hound = container.querySelector('[data-hound-pose]')
    expect(hound?.getAttribute('data-hound-pose')).toBe('run')
    expect(hound?.getAttribute('data-hound-frame')).toBe('2')
    expect(hound?.querySelector('image')).toBeNull()
    rerender(<svg><SkeletonHoundArt frame={5} knockedDown /></svg>)
    expect(container.querySelector('[data-hound-pose]')?.getAttribute('data-hound-pose')).toBe('knockdown')
  })
})
