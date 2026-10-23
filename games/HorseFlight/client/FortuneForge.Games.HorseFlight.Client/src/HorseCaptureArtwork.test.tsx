// @vitest-environment jsdom
import { render } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import { CaptureHorseArtwork, CaptureThrowArtwork, captureThrowWindow, type CaptureHazardKind } from './HorseCaptureArtwork'
import { captureProjectileGeometry, horseScreenX, type Obstacle } from './horseFlightSimulation'

const platformY = 460
const hazards: readonly { kind: CaptureHazardKind; throwSheet: string; captureSheet: string }[] = [
  {
    kind: 'lasso-thrower',
    throwSheet: 'horse-flight-lasso-miss-sheet-v1.png',
    captureSheet: 'horse-flight-lasso-capture-sheet-v1.png',
  },
  {
    kind: 'net-tower',
    throwSheet: 'horse-flight-net-miss-sheet-v1.png',
    captureSheet: 'horse-flight-net-capture-sheet-v1.png',
  },
]

function obstacle(kind: CaptureHazardKind, x: number): Obstacle {
  return {
    id: 1,
    biome: 'mountain',
    kind,
    platformId: 1,
    x,
    width: kind === 'lasso-thrower' ? 60 : 112,
    height: kind === 'lasso-thrower' ? 95 : 135,
    elevation: 0,
    additionalSpeed: 0,
  }
}

describe('Horse Flight painted capture hazards', () => {
  for (const { kind, throwSheet, captureSheet } of hazards) {
    it(`${kind} changes the atlas window for wind-up, flight, catch, and miss`, () => {
      const winding = captureThrowWindow(kind, 'winding', 0)
      const earlyFlight = captureThrowWindow(kind, 'flying', .2)
      const lateFlight = captureThrowWindow(kind, 'flying', .8)
      const caught = captureThrowWindow(kind, 'caught', 1)
      const grounded = captureThrowWindow(kind, 'grounded', 1)

      expect([winding.frame, earlyFlight.frame, lateFlight.frame, caught.frame, grounded.frame])
        .toEqual([0, 1, 2, 2, 3])
      expect(earlyFlight.sourceX).not.toBe(lateFlight.sourceX)
      expect(earlyFlight.sourceY).not.toBe(lateFlight.sourceY)
      expect(grounded.sourceY).toBeGreaterThan(winding.sourceY)
      expect(caught.cropLeft).toBeGreaterThan(0)
    })

    it(`${kind} paints the throw and its landed miss from one bitmap, without SVG-drawn rope or net`, () => {
      const startX = horseScreenX + 400
      const { container, rerender } = render(<svg>
        <CaptureThrowArtwork obstacle={obstacle(kind, startX)} platformY={platformY} caught={false} />
      </svg>)
      const art = () => container.querySelector('.ff-horse-flight__capture-throw-art')
      expect(art()?.getAttribute('data-capture-phase')).toBe('winding')
      expect(art()?.getAttribute('data-sprite-frame')).toBe('0')
      expect(art()?.querySelector('image')?.getAttribute('href'))
        .toContain(kind === 'lasso-thrower' ? 'horse-flight-animated-lasso-thrower-horizontal-v4.png' : throwSheet)
      expect(art()?.querySelector('.ff-horse-flight__capture-projectile-bitmap')).toBeNull()

      rerender(<svg><CaptureThrowArtwork obstacle={obstacle(kind, horseScreenX + 150)} platformY={platformY} caught={false} /></svg>)
      expect(art()?.getAttribute('data-capture-phase')).toBe('flying')
      const firstFlightWindow = art()?.querySelector('.ff-horse-flight__capture-actor-bitmap image')?.getAttribute('x')

      rerender(<svg><CaptureThrowArtwork obstacle={obstacle(kind, horseScreenX + 20)} platformY={platformY} caught={false} /></svg>)
      expect(art()?.getAttribute('data-capture-phase')).toBe('flying')
      expect(art()?.querySelector('.ff-horse-flight__capture-actor-bitmap image')?.getAttribute('x'))
        .not.toBe(firstFlightWindow)

      rerender(<svg><CaptureThrowArtwork obstacle={obstacle(kind, horseScreenX - 300)} platformY={platformY} caught={false} /></svg>)
      expect(art()?.getAttribute('data-capture-phase')).toBe('grounded')
      expect(art()?.getAttribute('data-sprite-frame')).toBe('3')
      const missImages = Array.from(art()?.querySelectorAll('image') ?? [])
      expect(missImages.length).toBeGreaterThan(0)
      expect(missImages.every(image => image.getAttribute('href')?.includes(throwSheet))).toBe(true)
      expect(Array.from(art()?.querySelectorAll('path, ellipse, circle, polygon, polyline, line, rect') ?? [])
        .every(shape => shape.closest('defs'))).toBe(true)

      rerender(<svg><CaptureThrowArtwork obstacle={obstacle(kind, horseScreenX + 20)} platformY={platformY} caught /></svg>)
      expect(art()?.getAttribute('data-capture-phase')).toBe('caught')
      const caughtImages = Array.from(art()?.querySelectorAll('image') ?? [])
      expect(caughtImages.length).toBeGreaterThan(0)
      expect(caughtImages.every(image => image.getAttribute('href')?.includes(throwSheet))).toBe(true)
      expect(Array.from(art()?.querySelectorAll('path, ellipse, circle, polygon, polyline, line, rect') ?? [])
        .every(shape => shape.closest('defs'))).toBe(true)
    })

    it(`${kind} keeps the visible projectile on its collision X and lands the miss on the platform`, () => {
      const nearContact = obstacle(kind, horseScreenX + 5)
      const collision = captureProjectileGeometry(nearContact, platformY)
      expect(collision?.phase).toBe('flying')
      const { container, rerender } = render(<svg>
        <CaptureThrowArtwork obstacle={nearContact} platformY={platformY} caught={false} />
      </svg>)
      const art = () => container.querySelector('.ff-horse-flight__capture-throw-art')
      const projectile = () => art()?.querySelector('.ff-horse-flight__capture-projectile-bitmap')
      const bitmapCenterX = nearContact.x + Number(projectile()?.getAttribute('x')) + Number(projectile()?.getAttribute('width')) / 2
      const bitmapCenterY = platformY - nearContact.elevation - nearContact.height
        + Number(projectile()?.getAttribute('y')) + Number(projectile()?.getAttribute('height')) / 2
      expect(projectile()).not.toBeNull()
      expect(bitmapCenterX).toBeCloseTo(collision!.x)
      expect(bitmapCenterX).toBeCloseTo(Number(art()?.getAttribute('data-projectile-center-x')))
      expect(bitmapCenterY).toBeCloseTo(Number(art()?.getAttribute('data-projectile-center-y')))
      // The elevated net throw eases toward the collision Y during flight.
      expect(Math.abs(bitmapCenterY - collision!.y)).toBeLessThan(5)

      const missed = obstacle(kind, horseScreenX - 300)
      rerender(<svg><CaptureThrowArtwork obstacle={missed} platformY={platformY} caught={false} /></svg>)
      expect(art()?.getAttribute('data-capture-phase')).toBe('grounded')
      const groundedBottomY = platformY - missed.elevation - missed.height
        + Number(projectile()?.getAttribute('y')) + Number(projectile()?.getAttribute('height'))
      expect(groundedBottomY).toBeCloseTo(platformY - 3)
      expect(projectile()?.querySelectorAll('image')).toHaveLength(1)
      expect(Array.from(art()?.querySelectorAll('path, ellipse, circle, polygon, polyline, line, rect') ?? [])
        .every(shape => shape.closest('defs'))).toBe(true)
    })

    it(`${kind} captures with one horse-inclusive bitmap that finishes on the platform`, () => {
      const horseY = platformY - 130
      const { container, rerender } = render(<svg>
        <CaptureHorseArtwork kind={kind} horseX={horseScreenX} horseY={horseY} platformY={platformY} reactionFrame={0} />
      </svg>)
      const art = () => container.querySelector('.ff-horse-flight__capture-horse-art')
      const airborneTop = Number(art()?.getAttribute('y'))
      const scale = kind === 'net-tower' ? .28 : .29
      expect(airborneTop + (kind === 'net-tower' ? 612 : 467) * scale).toBeCloseTo(horseY)
      expect(art()?.getAttribute('data-sprite-frame')).toBe(kind === 'net-tower' ? '1' : '0')
      expect(art()?.querySelector('image')?.getAttribute('href')).toContain(captureSheet)
      const frameWidth = kind === 'net-tower' ? 633 : 711
      const frameHeight = kind === 'net-tower' ? 621 : 553
      expect(art()?.getAttribute('viewBox')).toBe(`0 0 ${frameWidth} ${frameHeight}`)
      if (kind === 'net-tower') {
        expect(art()?.querySelector('clipPath path')?.getAttribute('d'))
          .toBe('M 0 0 H 633 V 621 H 0 V 240 H 64 V 160 H 0 Z')
      } else {
        expect(art()?.querySelector('clipPath rect')?.getAttribute('width')).toBe(String(frameWidth))
        expect(art()?.querySelector('clipPath rect')?.getAttribute('height')).toBe(String(frameHeight))
      }
      expect(art()?.querySelector('image')?.getAttribute('x')).toBe(kind === 'net-tower' ? '-633' : '0')

      rerender(<svg>
        <CaptureHorseArtwork kind={kind} horseX={horseScreenX} horseY={horseY} platformY={platformY} reactionFrame={4} />
      </svg>)
      expect(art()?.getAttribute('data-sprite-frame')).toBe('2')
      expect(Number(art()?.getAttribute('y')) + (kind === 'net-tower' ? 542 : 468) * scale)
        .toBeCloseTo(horseY + (platformY - horseY) * 4 / 7)

      rerender(<svg>
        <CaptureHorseArtwork kind={kind} horseX={horseScreenX} horseY={horseY} platformY={platformY} reactionFrame={7} />
      </svg>)
      expect(art()?.getAttribute('data-sprite-frame')).toBe('3')
      expect(art()?.querySelector('image')?.getAttribute('x')).toBe(String(-frameWidth))
      expect(art()?.querySelector('image')?.getAttribute('y')).toBe(String(-frameHeight))
      expect(Number(art()?.getAttribute('y')) + (kind === 'net-tower' ? 546 : 474) * scale)
        .toBeCloseTo(platformY)
      expect(art()?.querySelectorAll('image')).toHaveLength(1)
      expect(Array.from(art()?.querySelectorAll('path, ellipse, circle, polygon, polyline, line, rect') ?? [])
        .every(shape => shape.closest('defs'))).toBe(true)
    })
  }

  it('cycles the painted man-and-lasso wind-up before the projectile launches', () => {
    const winding = obstacle('lasso-thrower', horseScreenX + 400)
    const { container, rerender } = render(<svg>
      <CaptureThrowArtwork obstacle={winding} platformY={platformY} caught={false} animationFrame={0} />
    </svg>)
    const art = () => container.querySelector('.ff-horse-flight__capture-throw-art')
    const actor = () => art()?.querySelector('.ff-horse-flight__capture-actor-bitmap')
    const actorImage = () => actor()?.querySelector('image')
    const baseline = Number(actor()?.getAttribute('y')) + Number(actor()?.getAttribute('height'))

    for (const [animationFrame, expectedFrame] of [[2, 1], [4, 2], [6, 3], [8, 0]] as const) {
      rerender(<svg>
        <CaptureThrowArtwork obstacle={winding} platformY={platformY} caught={false} animationFrame={animationFrame} />
      </svg>)
      expect(art()?.getAttribute('data-capture-phase')).toBe('winding')
      expect(art()?.getAttribute('data-sprite-frame')).toBe(String(expectedFrame))
      expect(art()?.getAttribute('data-sprite-window-left')).toBe(String(expectedFrame * 140))
      expect(actorImage()?.getAttribute('x')).toBe(String(-expectedFrame * 140))
      expect(Number(actor()?.getAttribute('y')) + Number(actor()?.getAttribute('height'))).toBe(baseline)
      expect(art()?.querySelector('.ff-horse-flight__capture-projectile-bitmap')).toBeNull()
      expect(art()?.querySelector('.ff-horse-flight__capture-tether')).toBeNull()
    }

    rerender(<svg>
      <CaptureThrowArtwork obstacle={obstacle('lasso-thrower', horseScreenX + 150)} platformY={platformY} caught={false} animationFrame={6} />
    </svg>)
    expect(art()?.getAttribute('data-capture-phase')).toBe('flying')
    expect(actorImage()?.getAttribute('href')).toContain('horse-flight-lasso-miss-sheet-v1.png')
  })
})
