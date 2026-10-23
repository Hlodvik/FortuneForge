import { useId } from 'react'
import { captureProjectileGeometry, type CaptureProjectileGeometry, type Obstacle } from './horseFlightSimulation'
import { terminalGroundBaseline } from './horseSpriteScale'
import lassoMissSheet from './animated-obstacles/horse-flight-lasso-miss-sheet-v1.png?no-inline'
import lassoWindupSheet from './animated-obstacles/horse-flight-animated-lasso-thrower-horizontal-v4.png?no-inline'
import lassoCaptureSheet from './animated-obstacles/horse-flight-lasso-capture-sheet-v1.png?no-inline'
import netMissSheet from './animated-obstacles/horse-flight-net-miss-sheet-v1.png?no-inline'
import netCaptureSheet from './animated-obstacles/horse-flight-net-capture-sheet-v1.png?no-inline'

export type CaptureHazardKind = 'lasso-thrower' | 'net-tower'
type ProjectilePhase = CaptureProjectileGeometry['phase']

type SourceCrop = Readonly<{ x: number; y: number; width: number; height: number }>
type ActorFrame = SourceCrop & Readonly<{ handX: number; feetY: number }>

const throwAtlases: Record<CaptureHazardKind, Readonly<{
  sheet: string
  sheetWidth: number
  sheetHeight: number
  frameWidth: number
  actorScale: number
  actorFrames: readonly [ActorFrame, ActorFrame, ActorFrame, ActorFrame]
  flyingProjectile: readonly [SourceCrop, SourceCrop]
  groundedProjectile: SourceCrop
  projectileScale: number
  sourceXFraction: number
  sourceYOffset: number
}>> = {
  'lasso-thrower': {
    sheet: lassoMissSheet,
    sheetWidth: 1774,
    sheetHeight: 887,
    frameWidth: 887,
    actorScale: .33,
    actorFrames: [
      { x: 515, y: 0, width: 372, height: 443, handX: 190, feetY: 430 },
      { x: 1457, y: 0, width: 317, height: 443, handX: 62, feetY: 430 },
      { x: 570, y: 443, width: 317, height: 444, handX: 45, feetY: 375 },
      { x: 1457, y: 443, width: 317, height: 444, handX: 72, feetY: 375 },
    ],
    flyingProjectile: [
      { x: 925, y: 230, width: 170, height: 90 },
      { x: 50, y: 735, width: 175, height: 85 },
    ],
    groundedProjectile: { x: 927, y: 743, width: 190, height: 90 },
    projectileScale: .19,
    sourceXFraction: .74,
    sourceYOffset: 47,
  },
  'net-tower': {
    sheet: netMissSheet,
    sheetWidth: 1774,
    sheetHeight: 887,
    frameWidth: 887,
    actorScale: .4,
    actorFrames: [
      { x: 570, y: 0, width: 317, height: 443, handX: 142, feetY: 430 },
      { x: 1477, y: 0, width: 297, height: 443, handX: 80, feetY: 430 },
      { x: 580, y: 443, width: 307, height: 444, handX: 95, feetY: 410 },
      { x: 1477, y: 443, width: 297, height: 444, handX: 102, feetY: 410 },
    ],
    flyingProjectile: [
      { x: 130, y: 575, width: 350, height: 185 },
      { x: 130, y: 575, width: 350, height: 185 },
    ],
    groundedProjectile: { x: 907, y: 750, width: 420, height: 110 },
    projectileScale: .14,
    sourceXFraction: .48,
    sourceYOffset: 92,
  },
}

// The first four cells show the complete man-and-lasso wind-up. Cycling the
// painted cells moves both the arm and loop without drawing a second rope.
const lassoWindupAtlas = { sheet: lassoWindupSheet, sheetWidth: 1120, sheetHeight: 112 } as const
const lassoWindupFrameWidth = 140

function lassoWindupWindow(animationFrame: number) {
  const frame = Math.floor(Math.max(0, animationFrame) / 2) % 4
  return {
    atlas: lassoWindupAtlas,
    crop: { x: frame * lassoWindupFrameWidth, y: 0, width: lassoWindupFrameWidth, height: 112 },
    handX: 60,
    feetY: 106,
    scale: .93,
    frame,
  }
}

const captureAtlases = {
  'lasso-thrower': {
    sheet: lassoCaptureSheet,
    sheetWidth: 1422,
    sheetHeight: 1106,
    frameWidth: 711,
    frameHeight: 553,
    horseAnchorX: 270,
    groundBaselines: [467, 463, 468, 474],
    scale: .29,
  },
  'net-tower': {
    sheet: netCaptureSheet,
    sheetWidth: 1266,
    sheetHeight: 1242,
    frameWidth: 633,
    frameHeight: 621,
    horseAnchorX: 300,
    groundBaselines: [610, 612, 542, 546],
    scale: .28,
  },
} as const

/** Actor-only bitmap window. The projectile has its own cropped bitmap window. */
export function captureThrowWindow(kind: CaptureHazardKind, phase: ProjectilePhase, progress: number) {
  const atlas = throwAtlases[kind]
  const frame: 0 | 1 | 2 | 3 = phase === 'winding' ? 0
    : phase === 'caught' ? 2
      : phase === 'grounded' ? 3
        : progress < .55 ? 1 : 2
  const crop = atlas.actorFrames[frame]
  return {
    atlas,
    frame,
    cropLeft: crop.x - frame % 2 * atlas.frameWidth,
    sourceX: crop.x,
    sourceY: crop.y,
    sourceWidth: crop.width,
    sourceHeight: crop.height,
    handX: crop.handX,
    feetY: crop.feetY,
  }
}

function BitmapCrop({ atlas, crop, x, y, width, height, className }: Readonly<{
  atlas: Readonly<{ sheet: string; sheetWidth: number; sheetHeight: number }>
  crop: SourceCrop
  x: number
  y: number
  width: number
  height: number
  className: string
}>) {
  const clipId = useId()
  return <svg
    className={className}
    x={x}
    y={y}
    width={width}
    height={height}
    viewBox={`0 0 ${crop.width} ${crop.height}`}
    preserveAspectRatio="none"
    overflow="hidden"
    imageRendering="pixelated"
    aria-hidden="true"
  >
    <defs><clipPath id={clipId} clipPathUnits="userSpaceOnUse"><rect width={crop.width} height={crop.height} /></clipPath></defs>
    <g clipPath={`url(#${clipId})`}>
      <image href={atlas.sheet} x={-crop.x} y={-crop.y} width={atlas.sheetWidth} height={atlas.sheetHeight} preserveAspectRatio="none" />
    </g>
  </svg>
}

/** Separate painted pieces keep the hitbox, traveling mesh/loop, and thrower in step. */
export function CaptureThrowArtwork({ obstacle, platformY, caught, animationFrame = 0 }: Readonly<{
  obstacle: Obstacle
  platformY: number
  caught: boolean
  animationFrame?: number
}>) {
  if (obstacle.kind !== 'lasso-thrower' && obstacle.kind !== 'net-tower') return null
  const projectile = captureProjectileGeometry(obstacle, platformY, caught)
  if (!projectile) return null

  const kind = obstacle.kind
  const window = captureThrowWindow(kind, projectile.phase, projectile.progress)
  const { atlas } = window
  const heldLoop = kind === 'lasso-thrower' && projectile.phase === 'winding'
    ? lassoWindupWindow(animationFrame)
    : null
  const actorAtlas = heldLoop?.atlas ?? atlas
  const actorCrop = heldLoop?.crop ?? {
    x: window.sourceX, y: window.sourceY, width: window.sourceWidth, height: window.sourceHeight,
  }
  const actorScale = heldLoop?.scale ?? atlas.actorScale
  const sourceLocalX = obstacle.width * atlas.sourceXFraction
  const sourceLocalY = obstacle.height + obstacle.elevation - atlas.sourceYOffset
  // The parent obstacle group starts at (obstacle.x, platformY - elevation - height).
  const projectileLocalX = projectile.x - obstacle.x
  const actorX = sourceLocalX - (heldLoop?.handX ?? window.handX) * actorScale
  const actorY = obstacle.height + obstacle.elevation - (heldLoop?.feetY ?? window.feetY) * actorScale
  const projectileCrop = projectile.phase === 'grounded'
    ? atlas.groundedProjectile
    : atlas.flyingProjectile[projectile.progress < .55 ? 0 : 1]
  const projectileWidth = projectileCrop.width * atlas.projectileScale
  const projectileHeight = projectileCrop.height * atlas.projectileScale
  const showProjectile = projectile.phase === 'flying' || projectile.phase === 'grounded'
  // The net leaves the elevated hand before it converges with the deterministic
  // hitbox. A missed bitmap lands *on* the platform rather than halfway through it.
  const visualProjectileY = projectile.phase === 'grounded'
    ? platformY - 3 - projectileHeight / 2
    : kind === 'net-tower' && projectile.phase === 'flying'
      ? projectile.y - 40 * (1 - projectile.progress)
      : projectile.y
  const projectileLocalY = visualProjectileY - platformY + obstacle.elevation + obstacle.height

  const ropeStartX = projectileLocalX + projectile.width * .3
  const ropeStartY = projectileLocalY
  const ropeDx = sourceLocalX - ropeStartX
  const ropeDy = sourceLocalY - ropeStartY
  const ropeLength = Math.hypot(ropeDx, ropeDy)
  const ropeAngle = Math.atan2(ropeDy, ropeDx) * 180 / Math.PI

  return <g
    className="ff-horse-flight__capture-throw-art"
    data-capture-kind={kind}
    data-capture-phase={projectile.phase}
    data-sprite-frame={heldLoop?.frame ?? window.frame}
    data-sprite-window-left={heldLoop?.crop.x ?? window.cropLeft}
    data-projectile-center-x={showProjectile ? projectile.x : undefined}
    data-projectile-center-y={showProjectile ? visualProjectileY : undefined}
    data-collision-center-y={showProjectile ? projectile.y : undefined}
    aria-hidden="true"
  >
    {kind === 'lasso-thrower' && showProjectile && ropeLength > 4 && <g
      className="ff-horse-flight__capture-tether"
      transform={`translate(${ropeStartX} ${ropeStartY}) rotate(${ropeAngle})`}
    >
      <BitmapCrop
        atlas={atlas}
        crop={{ x: 1090, y: 255, width: 410, height: 58 }}
        x={0}
        y={-5.5}
        width={ropeLength}
        height={11}
        className="ff-horse-flight__capture-tether-bitmap"
      />
    </g>}
    {showProjectile && <BitmapCrop
      atlas={atlas}
      crop={projectileCrop}
      x={projectileLocalX - projectileWidth / 2}
      y={projectileLocalY - projectileHeight / 2}
      width={projectileWidth}
      height={projectileHeight}
      className="ff-horse-flight__capture-projectile-bitmap"
    />}
    <BitmapCrop
      atlas={actorAtlas}
      crop={actorCrop}
      x={actorX}
      y={actorY}
      width={actorCrop.width * actorScale}
      height={actorCrop.height * actorScale}
      className="ff-horse-flight__capture-actor-bitmap"
    />
  </g>
}

/** Capture art includes the horse and the actual rope/net; never paint a second horse underneath it. */
export function CaptureHorseArtwork({ kind, horseX, horseY, platformY, reactionFrame }: Readonly<{
  kind: CaptureHazardKind
  horseX: number
  horseY: number
  platformY: number
  reactionFrame: number
}>) {
  const clipId = useId()
  const atlas = captureAtlases[kind]
  const step = Math.min(7, Math.max(0, Math.floor(reactionFrame)))
  // The first net image bleeds into its neighboring atlas cell. Begin at its
  // clean second pose while preserving the full collision-to-ground timing.
  const frame = kind === 'net-tower' ? ([1, 1, 1, 2, 2, 2, 3, 3] as const)[step]
    : Math.floor(step / 2)
  const frameLeft = frame % 2 * atlas.frameWidth
  const frameTop = Math.floor(frame / 2) * atlas.frameHeight
  const baselineY = terminalGroundBaseline(horseY, platformY, step)
  return <svg
    className="ff-horse-flight__horse-art ff-horse-flight__capture-horse-art"
    data-terminal-horse-sprite={kind === 'lasso-thrower' ? 'lasso-caught' : 'net-caught'}
    data-sprite-frame={frame}
    x={horseX - atlas.horseAnchorX * atlas.scale}
    y={baselineY - atlas.groundBaselines[frame] * atlas.scale}
    width={atlas.frameWidth * atlas.scale}
    height={atlas.frameHeight * atlas.scale}
    viewBox={`0 0 ${atlas.frameWidth} ${atlas.frameHeight}`}
    preserveAspectRatio="none"
    overflow="hidden"
    imageRendering="pixelated"
    aria-hidden="true"
  >
    <defs><clipPath id={clipId} clipPathUnits="userSpaceOnUse">
      {kind === 'net-tower' && frame === 1
        // The outgoing net in cell 0 spills 59 source pixels into cell 1.
        // Notch only that empty upper-left area; the captured horse is below it.
        ? <path d={`M 0 0 H ${atlas.frameWidth} V ${atlas.frameHeight} H 0 V 240 H 64 V 160 H 0 Z`} />
        : <rect width={atlas.frameWidth} height={atlas.frameHeight} />}
    </clipPath></defs>
    <g clipPath={`url(#${clipId})`}>
      <image href={atlas.sheet} x={-frameLeft} y={-frameTop} width={atlas.sheetWidth} height={atlas.sheetHeight} preserveAspectRatio="none" />
    </g>
  </svg>
}
