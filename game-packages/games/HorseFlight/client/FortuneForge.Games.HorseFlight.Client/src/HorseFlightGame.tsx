import { useCallback, useEffect, useRef, useState } from 'react'
import { HorseFlightGatewayError, type HorseFlightGateway, type HorseFlightResult, type HorseFlightStatus } from './contracts'
import { HttpHorseFlightGateway } from './httpHorseFlightGateway'
import { hauntedBiomeBlend, horseScreenX, startHorse, stepHorse, type HorseState, type ObstacleKind } from './horseFlightSimulation'
import horseDashSpriteSheet from './horse-flight-dash-sprite-sheet-v1.png?no-inline'
import horseJumpSpriteSheet from './horse-flight-jump-sprite-sheet-v2.png?no-inline'
import horseRearSpriteSheet from './horse-flight-rear-sprite-sheet-v1.png?no-inline'
import horseMountainBackground from './horse-flight-mountain-background-v1.png?no-inline'
import horseMedievalMidground from './horse-flight-medieval-midground-4x-v1.png?no-inline'
import horseHauntedBackground from './horse-flight-haunted-background-v1.png?no-inline'
import horseHauntedMidground from './horse-flight-haunted-midground-v1.png?no-inline'
import horseRunSpriteSheet from './horse-flight-run-sprite-sheet-v2.png?no-inline'
import obstacleBoulder from './obstacles/horse-flight-obstacle-boulder-v1.png?no-inline'
import obstacleCrate from './obstacles/horse-flight-obstacle-crate-single-v1.png?no-inline'
import obstacleCrateCluster from './obstacles/horse-flight-obstacle-crate-double-v1.png?no-inline'
import obstacleFallenLog from './obstacles/horse-flight-obstacle-fallen-log-v3.png?no-inline'
import obstacleFence from './obstacles/horse-flight-obstacle-fence-v2.png?no-inline'
import obstacleOilSpill from './obstacles/horse-flight-obstacle-oil-spill-v1.png?no-inline'
import obstaclePit from './obstacles/horse-flight-obstacle-pit-v1.png?no-inline'
import obstacleWell from './obstacles/horse-flight-obstacle-well-v3.png?no-inline'
import obstacleBats from './animated-obstacles/horse-flight-animated-bats-horizontal-v2.png?no-inline'
import obstacleLassoKnockdown from './animated-obstacles/horse-flight-animated-lasso-knockdown-horizontal-v1.png?no-inline'
import obstacleLassoThrower from './animated-obstacles/horse-flight-animated-lasso-thrower-horizontal-v3.png?no-inline'
import obstacleNetTower from './animated-obstacles/horse-flight-animated-net-tower-horizontal-v1.png?no-inline'
import obstacleRollingBarrel from './animated-obstacles/horse-flight-animated-rolling-barrel-horizontal-v3.png?no-inline'
import obstacleSkeletonDog from './animated-obstacles/horse-flight-animated-skeleton-dog-horizontal-v1.png?no-inline'
import obstacleSkeletonDogKnockdown from './animated-obstacles/horse-flight-animated-skeleton-dog-knockdown-horizontal-v1.png?no-inline'
import obstacleSkeleton from './animated-obstacles/horse-flight-animated-skeleton-horizontal-v1.png?no-inline'
import './horseFlight.css'
import './horseFlightArt.css'

const defaultGateway = new HttpHorseFlightGateway()
const horseSpriteFrameWidth = 2172 / 8
const horseSpriteFrameTop = 240
const horseSpriteFrameHeight = 224
const horseSpriteSheetHeight = 724
const runningTrackTop = 460
const rearingHoldMilliseconds = 1_250
const openingRunInTicks = 42
const rearingHorseCenterX = 960 * 0.08
const runningHorseArtWidth = 55
// Keep the rearing pose on the same ground line and at the same on-screen scale
// as the normal running sheet. The generated rear frames fill almost their entire
// 240×256 cell, whereas the running art has generous frame padding.
const rearingHorseArtWidth = 55
const rearingHorseArtHeight = 45
const rearingSpriteFrameWidth = 240
const rearingSpriteFrameHeight = 256
const rearingSpriteSheetWidth = rearingSpriteFrameWidth * 6
const rearingFrameMilliseconds = 120
const runningFrameMilliseconds = 90
type HorseSpriteFrame = Readonly<{ sheet: string; index: number }>
type ObstacleSpriteSheet = Readonly<{
  width: number
  height: number
  columns: number
  rows: number
  ticksPerFrame: number
  frameCrop?: Readonly<{ x: number; y: number; width: number; height: number }>
}>
type StaticObstacleCrop = Readonly<{ sourceWidth: number; sourceHeight: number; x: number; y: number; width: number; height: number }>
type ObstacleArtwork = Readonly<{
  source: string
  label: string
  spriteSheet?: ObstacleSpriteSheet
  knockdownSource?: string
  knockdownSpriteSheet?: ObstacleSpriteSheet
  staticCrop?: StaticObstacleCrop
}>

const obstacleArtwork: Readonly<Record<ObstacleKind, ObstacleArtwork>> = {
  crate: { source: obstacleCrate, label: 'tombstone' },
  'crate-cluster': { source: obstacleCrateCluster, label: 'tombstone cluster' },
  pit: { source: obstaclePit, label: 'sunken pit', staticCrop: { sourceWidth: 1774, sourceHeight: 887, x: 60, y: 304, width: 1654, height: 355 } },
  well: { source: obstacleWell, label: 'well', staticCrop: { sourceWidth: 112, sourceHeight: 104, x: 8, y: 12, width: 96, height: 88 } },
  fence: { source: obstacleFence, label: 'fence', staticCrop: { sourceWidth: 354, sourceHeight: 177, x: 76, y: 59, width: 203, height: 66 } },
  carriage: { source: obstacleBoulder, label: 'wooden obstacle', staticCrop: { sourceWidth: 1536, sourceHeight: 1024, x: 275, y: 275, width: 987, height: 518 } },
  'oil-spill': { source: obstacleOilSpill, label: 'green grave goo' },
  boulder: { source: obstacleBoulder, label: 'boulder', staticCrop: { sourceWidth: 1536, sourceHeight: 1024, x: 275, y: 275, width: 987, height: 518 } },
  'fallen-log': { source: obstacleFallenLog, label: 'fallen log', staticCrop: { sourceWidth: 160, sourceHeight: 80, x: 4, y: 20, width: 144, height: 40 } },
  dog: {
    source: obstacleSkeletonDog,
    label: 'skeleton hound',
    spriteSheet: { width: 2172, height: 724, columns: 6, rows: 1, ticksPerFrame: 3, frameCrop: { x: 0, y: 180, width: 362, height: 360 } },
    knockdownSource: obstacleSkeletonDogKnockdown,
    knockdownSpriteSheet: { width: 2172, height: 724, columns: 6, rows: 1, ticksPerFrame: 2, frameCrop: { x: 0, y: 180, width: 362, height: 360 } },
  },
  'bat-flock': { source: obstacleBats, label: 'flying bats', spriteSheet: { width: 1152, height: 128, columns: 6, rows: 1, ticksPerFrame: 3 } },
  'lasso-thrower': {
    source: obstacleLassoThrower,
    label: 'lasso thrower',
    spriteSheet: { width: 1120, height: 112, columns: 8, rows: 1, ticksPerFrame: 4 },
    knockdownSource: obstacleLassoKnockdown,
    knockdownSpriteSheet: { width: 768, height: 112, columns: 6, rows: 1, ticksPerFrame: 2 },
  },
  'net-tower': { source: obstacleNetTower, label: 'net tower', spriteSheet: { width: 1024, height: 160, columns: 8, rows: 1, ticksPerFrame: 4 } },
  skeleton: { source: obstacleSkeleton, label: 'shambling skeleton', spriteSheet: { width: 768, height: 112, columns: 6, rows: 1, ticksPerFrame: 3 } },
  'rolling-barrel': { source: obstacleRollingBarrel, label: 'rolling barrel', spriteSheet: { width: 768, height: 128, columns: 4, rows: 1, ticksPerFrame: 3 } },
}

export type HorseFlightGameProps = Readonly<{ gateway?: HorseFlightGateway; playerId?: string }>

export function HorseFlightGame({ gateway = defaultGateway }: HorseFlightGameProps) {
  const [status, setStatus] = useState<HorseFlightStatus | null>(null)
  const [game, setGame] = useState<HorseState | null>(null)
  const [result, setResult] = useState<HorseFlightResult | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [tableUnavailable, setTableUnavailable] = useState(false)
  const [statusLoadAttempt, setStatusLoadAttempt] = useState(0)
  const [openingPose, setOpeningPose] = useState(false)
  const [rearingFrame, setRearingFrame] = useState(0)
  const [runningFrame, setRunningFrame] = useState(0)
  const gameRef = useRef<HorseState | null>(null)
  const runId = useRef<string | null>(null)
  const queuedJump = useRef(false)
  const queuedRightClick = useRef(false)
  const dashHeld = useRef(false)
  const jumps = useRef<number[]>([])
  const rightClicks = useRef<number[]>([])
  const submitted = useRef(false)
  const field = useRef<HTMLElement | null>(null)
  const startRequestKey = useRef<string | null>(null)
  const completionRequestKey = useRef<string | null>(null)

  const accept = useCallback((next: HorseState) => {
    gameRef.current = next
    setGame(next)
  }, [])

  useEffect(() => {
    const controller = new AbortController()
    void gateway.getStatus(controller.signal).then(nextStatus => {
      setStatus(nextStatus)
      setTableUnavailable(!nextStatus.available)
      setError(nextStatus.available ? null : 'Horse Flight is temporarily unavailable. Please choose another game.')
    }).catch(reason => {
      if (reason instanceof DOMException && reason.name === 'AbortError') return
      const unavailable = isTableUnavailable(reason)
      setTableUnavailable(unavailable)
      setError(unavailable ? 'Horse Flight is temporarily unavailable. Please choose another game.' : messageForError(reason, 'Horse Flight is unavailable.'))
    })
    return () => controller.abort()
  }, [gateway, statusLoadAttempt])

  const start = useCallback(async () => {
    if (!status?.available) return
    setError(null)
    setResult(null)
    submitted.current = false
    queuedJump.current = false
    queuedRightClick.current = false
    dashHeld.current = false
    jumps.current = []
    rightClicks.current = []
    const idempotencyKey = startRequestKey.current ??= key('start')
    try {
      const run = await gateway.start({ idempotencyKey })
      startRequestKey.current = null
      completionRequestKey.current = null
      runId.current = run.runId
      setOpeningPose(true)
      accept(startHorse(run.seed))
      requestAnimationFrame(() => field.current?.focus())
    } catch (reason) {
      setError(messageForError(reason, 'Horse Flight could not start.'))
    }
  }, [accept, gateway, status?.available])

  useEffect(() => {
    if (game?.phase !== 'running') {
      setOpeningPose(false)
      return undefined
    }

    setOpeningPose(true)
    const timer = window.setTimeout(() => setOpeningPose(false), rearingHoldMilliseconds)
    return () => window.clearTimeout(timer)
  }, [game?.phase])

  useEffect(() => {
    if (!openingPose || game?.phase !== 'running') {
      setRearingFrame(0)
      return undefined
    }

    setRearingFrame(0)
    const timer = window.setInterval(
      () => setRearingFrame(frame => Math.min(frame + 1, 5)),
      rearingFrameMilliseconds)
    return () => window.clearInterval(timer)
  }, [game?.phase, openingPose])

  useEffect(() => {
    const releaseDash = (event: MouseEvent) => {
      if (event.button === 2) dashHeld.current = false
    }
    window.addEventListener('mouseup', releaseDash)
    return () => window.removeEventListener('mouseup', releaseDash)
  }, [])

  useEffect(() => {
    if (openingPose || game?.phase !== 'running') {
      setRunningFrame(0)
      return undefined
    }

    const timer = window.setInterval(
      () => setRunningFrame(frame => (frame + 1) % 8),
      runningFrameMilliseconds)
    return () => window.clearInterval(timer)
  }, [game?.phase, openingPose])

  const complete = useCallback((terminal: HorseState) => {
    const currentRunId = runId.current
    if (submitted.current || currentRunId === null) return
    submitted.current = true
    const idempotencyKey = completionRequestKey.current ??= key('complete')
    const submission = {
      runId: currentRunId,
      totalTicks: terminal.tick,
      jumpTicks: [...jumps.current],
      rightClickTicks: [...rightClicks.current],
      idempotencyKey,
    }
    void gateway.complete(submission.runId, submission.totalTicks, submission.jumpTicks, { idempotencyKey }, submission.rightClickTicks).then(response => {
      completionRequestKey.current = null
      setResult(response)
      setError(null)
    }).catch(reason => {
      submitted.current = false
      setError(messageForError(reason, 'Horse Flight score could not be recorded.'))
    })
  }, [gateway])

  useEffect(() => {
    if (!status || !game || game.phase !== 'running' || openingPose) return undefined
    const timer = window.setInterval(() => {
      const current = gameRef.current
      if (!current || current.phase !== 'running') return
      const wantsDash = dashHeld.current || queuedRightClick.current
      const next = stepHorse(current, queuedJump.current, wantsDash)
      queuedJump.current = false
      queuedRightClick.current = false
      if (next.jumped) jumps.current.push(current.tick)
      if (next.slid || next.fastFell) rightClicks.current.push(current.tick)
      accept(next.state)
      if (next.state.phase !== 'running') complete(next.state)
    }, status.tickMilliseconds)
    return () => window.clearInterval(timer)
  }, [accept, complete, game?.phase, openingPose, status])

  const jump = () => {
    if (!openingPose && gameRef.current?.phase === 'running') {
      queuedJump.current = true
      queuedRightClick.current = false
      dashHeld.current = false
    }
  }

  const beginDash = () => {
    if (!openingPose && gameRef.current?.phase === 'running') {
      dashHeld.current = true
      queuedRightClick.current = true
      queuedJump.current = false
    }
  }

  const endDash = () => { dashHeld.current = false }

  const retryStatus = () => {
    setError(null)
    setTableUnavailable(false)
    setStatusLoadAttempt(attempt => attempt + 1)
  }
  const horseSpriteFrame = game && !openingPose ? spriteFrameFor(game, runningFrame) : null
  const runInProgress = !openingPose && game?.phase === 'running'
  const runInProgressRatio = runInProgress ? Math.min(game.tick / openingRunInTicks, 1) : 1
  const horseCenterX = openingPose
    ? rearingHorseCenterX
    : rearingHorseCenterX + ((horseScreenX - rearingHorseCenterX) * runInProgressRatio)
  const hauntedSceneOpacity = game ? hauntedBiomeBlend(game.tick) : 0
  const activeBiome = game?.biome ?? 'mountain'

  return <main className="ff-horse-flight" data-active-biome={activeBiome}>
    <header className="ff-horse-flight__header">
      <div><small>{activeBiome === 'haunted' ? 'Haunted trail' : 'Mountain pass'}</small><h1>Horse Flight</h1></div>
      <p><b>Space / click</b> to double-jump. <b>Hold right-click</b> to slide under low hazards or fast-fall in mid-air.</p>
    </header>
    {!game && <section className="ff-horse-flight__lobby">
      <div className="ff-horse-flight__lobby-scene" style={{ backgroundImage: `linear-gradient(180deg, rgb(5 11 23 / 12%) 20%, rgb(5 11 23 / 72%) 100%), url(${horseMountainBackground})` }} aria-hidden="true">
        <div className="ff-horse-flight__lobby-copy"><span>Mountain pass</span><strong>Ready to ride?</strong></div>
      </div>
      {result && <p role="status">Previous run saved. Official score: <strong>{result.score}</strong>.</p>}
      {!status && error && !tableUnavailable && <button className="ff-horse-flight__start" onClick={retryStatus} type="button">Retry connection</button>}
      <button className="ff-horse-flight__start" disabled={!status?.available} onClick={() => void start()} type="button">{status?.available ? 'Start run' : tableUnavailable ? 'Run unavailable' : 'Connecting…'}</button>
    </section>}
    {game && <>
      <div className="ff-horse-flight__stats"><span>Score <b>{game.score}</b></span><span>Level <b>{1 + Math.floor(game.score / 500)}</b></span><span>Jumps <b>{game.jumpsRemaining}</b></span><span>Dash <b>{game.slideTicksRemaining > 0 ? 'On' : 'Ready'}</b></span><span className="ff-horse-flight__biome">{activeBiome === 'haunted' ? 'Haunted trail' : 'Mountain pass'}</span></div>
      <section className="ff-horse-flight__field" ref={field} tabIndex={0} aria-label="Horse Flight playfield" onMouseDown={event => { if (event.button === 0) { field.current?.focus(); jump() } else if (event.button === 2) { event.preventDefault(); field.current?.focus(); beginDash() } }} onMouseUp={event => { if (event.button === 2) endDash() }} onMouseLeave={endDash} onContextMenu={event => event.preventDefault()} onKeyDown={event => { if (event.code === 'Space' && !event.repeat) { event.preventDefault(); jump() } }}>
        <svg viewBox="0 0 960 540" role="img" aria-label="Horse and platforms" data-active-biome={activeBiome}>
          <defs>
            <pattern id="horse-flight-earth" width="32" height="32" patternUnits="userSpaceOnUse">
              <rect width="32" height="32" fill="#181a27" />
              <rect width="32" height="3" fill="#3b4051" />
              <rect y="3" width="32" height="4" fill="#282c3d" />
              <rect x="2" y="9" width="8" height="2" fill="#303548" /><rect x="17" y="8" width="11" height="2" fill="#25293a" />
              <rect x="8" y="15" width="6" height="2" fill="#353a4d" /><rect x="24" y="18" width="5" height="2" fill="#2d3143" />
              <rect x="1" y="24" width="12" height="2" fill="#10121c" /><rect x="17" y="27" width="9" height="2" fill="#24283a" />
            </pattern>
            <pattern id="horse-flight-grass" width="28" height="14" patternUnits="userSpaceOnUse">
              <rect width="28" height="14" fill="#609b14" />
              <rect y="3" width="28" height="4" fill="#86c828" /><rect y="8" width="28" height="6" fill="#426f12" />
              <rect x="1" width="2" height="5" fill="#b9ec42" /><rect x="7" y="1" width="2" height="4" fill="#9cda31" />
              <rect x="15" width="2" height="5" fill="#b9ec42" /><rect x="23" y="1" width="2" height="4" fill="#9cda31" />
              <rect x="4" y="7" width="6" height="2" fill="#75b51d" /><rect x="18" y="7" width="7" height="2" fill="#75b51d" />
            </pattern>
            <linearGradient id="horse-flight-coat" x2="0" y2="1"><stop stopColor="#f3c76a" /><stop offset="1" stopColor="#9f572a" /></linearGradient>
            <clipPath id="horse-flight-scene"><rect width="960" height={runningTrackTop} /></clipPath>
          </defs>
          <g clipPath="url(#horse-flight-scene)" aria-hidden="true">
            <g opacity={1 - hauntedSceneOpacity} data-biome="mountain">
              <g className="ff-horse-flight__background-pan"><image href={horseMountainBackground} x="-330" y="0" width="1620" height={runningTrackTop} preserveAspectRatio="none" /></g>
              <g className="ff-horse-flight__midground-pan"><image href={horseMedievalMidground} x="0" y="0" width="6480" height={runningTrackTop} preserveAspectRatio="none" /></g>
            </g>
            <g opacity={hauntedSceneOpacity} data-biome="haunted">
              <g className="ff-horse-flight__background-pan"><image href={horseHauntedBackground} x="-330" y="0" width="1620" height={runningTrackTop} preserveAspectRatio="none" /></g>
              <g className="ff-horse-flight__midground-pan"><image href={horseHauntedMidground} x="0" y="0" width="6480" height={runningTrackTop} preserveAspectRatio="none" /></g>
            </g>
          </g>
          {game.platforms.map(platform => {
            const groundHazards = game.obstacles.filter(obstacle =>
              obstacle.platformId === platform.id && (obstacle.kind === 'pit' || obstacle.kind === 'oil-spill'))
            return <g key={platform.id}>
              <rect className="ff-horse-flight__platform" x={platform.x} y={platform.y} width={platform.width} height={540 - platform.y} fill="url(#horse-flight-earth)" />
              <rect className="ff-horse-flight__grass" x={platform.x} y={platform.y - 7} width={platform.width} height="13" fill="url(#horse-flight-grass)" />
              {groundHazards.map(obstacle => obstacle.kind === 'pit'
                ? <g key={obstacle.id} className="ff-horse-flight__ground-treatment" data-ground-treatment="pit">
                    <rect x={obstacle.x + 4} y={platform.y - 7} width={obstacle.width - 8} height="17" fill="#1a1215" />
                    <rect x={obstacle.x + 8} y={platform.y - 3} width={obstacle.width - 16} height="11" fill="#090c13" />
                    <rect x={obstacle.x + 3} y={platform.y - 8} width={obstacle.width - 6} height="3" fill="#4d301a" />
                  </g>
                : <g key={obstacle.id} className="ff-horse-flight__ground-treatment" data-ground-treatment="grave-goo">
                    <ellipse cx={obstacle.x + obstacle.width / 2} cy={platform.y - 2} rx={obstacle.width / 2 - 2} ry="6" fill="#17371f" />
                    <ellipse cx={obstacle.x + obstacle.width * 0.37} cy={platform.y - 4} rx={obstacle.width * 0.19} ry="2" fill="#4d9c3f" />
                    <rect x={obstacle.x + obstacle.width * 0.64} y={platform.y - 4} width={obstacle.width * 0.12} height="2" fill="#a4e75b" />
                  </g>)}
            </g>
          })}
          {game.obstacles.map(obstacle => {
            const platform = game.platforms.find(item => item.id === obstacle.platformId)
            const artwork = obstacleArtwork[obstacle.kind]
            const isKnockedDown = obstacle.knockedDownAt !== undefined && artwork.knockdownSpriteSheet !== undefined
            const spriteSheet = isKnockedDown ? artwork.knockdownSpriteSheet : artwork.spriteSheet
            const spriteSource = isKnockedDown ? artwork.knockdownSource! : artwork.source
            const spriteFrame = spriteSheet ? obstacleSpriteFrameFor(spriteSheet, game.tick, obstacle.id, obstacle.knockedDownAt) : null
            const staticCrop = artwork.staticCrop
            return platform && <g key={obstacle.id} className="ff-horse-flight__obstacle" data-obstacle-kind={obstacle.kind} data-knocked-down={isKnockedDown || undefined} aria-label={artwork.label} transform={`translate(${obstacle.x} ${platform.y - obstacle.elevation - obstacle.height})`}>
              {obstacle.kind === 'crate' || obstacle.kind === 'crate-cluster'
                ? <TombstoneObstacle width={obstacle.width} height={obstacle.height} cluster={obstacle.kind === 'crate-cluster'} />
                : obstacle.kind === 'oil-spill'
                  ? <GreenGooObstacle width={obstacle.width} height={obstacle.height} />
                : spriteFrame
                ? <svg x="0" y="0" width={obstacle.width} height={obstacle.height} viewBox={`0 0 ${spriteFrame.width} ${spriteFrame.height}`} preserveAspectRatio="none" overflow="hidden">
                    <image href={spriteSource} x={-spriteFrame.x} y={-spriteFrame.y} width={spriteFrame.sheetWidth} height={spriteFrame.sheetHeight} preserveAspectRatio="none" />
                  </svg>
                : staticCrop
                  ? <svg x="0" y="0" width={obstacle.width} height={obstacle.height} viewBox={`${staticCrop.x} ${staticCrop.y} ${staticCrop.width} ${staticCrop.height}`} preserveAspectRatio="none" overflow="hidden">
                      <image href={artwork.source} x="0" y="0" width={staticCrop.sourceWidth} height={staticCrop.sourceHeight} preserveAspectRatio="none" />
                    </svg>
                : <image href={artwork.source} x="0" y="0" width={obstacle.width} height={obstacle.height} preserveAspectRatio="none" />}
            </g>
          })}
          {openingPose && <svg className="ff-horse-flight__horse-art" x={horseCenterX - (rearingHorseArtWidth / 2)} y={game.horseY - 44} width={rearingHorseArtWidth} height={rearingHorseArtHeight} viewBox={`0 0 ${rearingSpriteFrameWidth} ${rearingSpriteFrameHeight}`} preserveAspectRatio="xMidYMax meet" overflow="hidden" data-sprite-frame={rearingFrame} aria-hidden="true">
            <defs><clipPath id="horse-flight-rear-frame"><rect width={rearingSpriteFrameWidth} height={rearingSpriteFrameHeight} /></clipPath></defs>
            <image clipPath="url(#horse-flight-rear-frame)" href={horseRearSpriteSheet} x={-rearingFrame * rearingSpriteFrameWidth} y="0" width={rearingSpriteSheetWidth} height={rearingSpriteFrameHeight} preserveAspectRatio="none" />
          </svg>}
          {horseSpriteFrame && <svg className="ff-horse-flight__horse-art" x={horseCenterX - (runningHorseArtWidth / 2)} y={game.horseY - 44} width={runningHorseArtWidth} height="45" viewBox={`0 ${horseSpriteFrameTop} ${horseSpriteFrameWidth} ${horseSpriteFrameHeight}`} preserveAspectRatio="none" overflow="hidden" data-sprite-frame={horseSpriteFrame.index} aria-hidden="true">
            <defs><clipPath id="horse-flight-running-frame"><rect x="0" y={horseSpriteFrameTop} width={horseSpriteFrameWidth} height={horseSpriteFrameHeight} /></clipPath></defs>
            <image clipPath="url(#horse-flight-running-frame)" href={horseSpriteFrame.sheet} x={-horseSpriteFrame.index * horseSpriteFrameWidth} y="0" width="2172" height={horseSpriteSheetHeight} preserveAspectRatio="none" />
          </svg>}
        </svg>
        {game.phase !== 'running' && <div className="ff-horse-flight__over" role="status" aria-live="assertive">
          <strong>Run ended</strong>
          <span>{game.phase === 'obstacle-collision' ? 'You hit an obstacle.' : 'The horse left the track.'}</span>
          <strong>Score {result?.score ?? game.score}</strong>
          <span>{result ? 'Score saved to the leaderboard.' : error ? 'Save failed' : 'Saving score…'}</span>
          {!result && error && <button onClick={() => complete(game)} type="button">Try saving again</button>}
          {result && <button onClick={() => void start()} type="button">Start fresh run</button>}
        </div>}
      </section>
    </>}
    {error && <p role="alert" className="ff-horse-flight__error">{error}</p>}
  </main>
}

function TombstoneObstacle({ width, height, cluster }: Readonly<{ width: number; height: number; cluster: boolean }>) {
  const mainWidth = cluster ? width * 0.58 : width * 0.74
  const mainX = cluster ? width * 0.07 : (width - mainWidth) / 2
  const mainHeight = height * 0.88
  const mainY = height - mainHeight
  return <g shapeRendering="crispEdges" data-artwork="tombstone">
    {cluster && <g opacity=".92">
      <rect x={width * 0.56} y={height * 0.27} width={width * 0.32} height={height * 0.62} fill="#2d303e" />
      <rect x={width * 0.61} y={height * 0.2} width={width * 0.22} height={height * 0.16} fill="#41485c" />
      <rect x={width * 0.64} y={height * 0.41} width={width * 0.16} height={height * 0.05} fill="#70788d" />
    </g>}
    <rect x={mainX} y={mainY + mainHeight * 0.16} width={mainWidth} height={mainHeight * 0.84} fill="#252a37" />
    <rect x={mainX + mainWidth * 0.12} y={mainY + mainHeight * 0.07} width={mainWidth * 0.76} height={mainHeight * 0.18} fill="#3e4658" />
    <rect x={mainX + mainWidth * 0.1} y={mainY + mainHeight * 0.21} width={mainWidth * 0.8} height={mainHeight * 0.69} fill="#4c5568" />
    <rect x={mainX + mainWidth * 0.18} y={mainY + mainHeight * 0.28} width={mainWidth * 0.64} height={mainHeight * 0.08} fill="#788396" />
    <rect x={mainX + mainWidth * 0.43} y={mainY + mainHeight * 0.4} width={mainWidth * 0.14} height={mainHeight * 0.29} fill="#a2adbb" />
    <rect x={mainX + mainWidth * 0.3} y={mainY + mainHeight * 0.49} width={mainWidth * 0.4} height={mainHeight * 0.12} fill="#a2adbb" />
    <rect x={mainX - mainWidth * 0.1} y={height - height * 0.1} width={mainWidth * 1.2} height={height * 0.1} fill="#202631" />
  </g>
}

function GreenGooObstacle({ width, height }: Readonly<{ width: number; height: number }>) {
  return <g shapeRendering="crispEdges" data-artwork="grave-goo">
    <ellipse cx={width / 2} cy={height * 0.69} rx={width * 0.48} ry={height * 0.31} fill="#174526" />
    <ellipse cx={width * 0.4} cy={height * 0.48} rx={width * 0.23} ry={height * 0.2} fill="#58a843" />
    <rect x={width * 0.67} y={height * 0.36} width={width * 0.11} height={height * 0.17} fill="#b9ef61" />
    <rect x={width * 0.2} y={height * 0.67} width={width * 0.14} height={height * 0.1} fill="#83ca4e" />
  </g>
}

function key(action: 'start' | 'complete') { const random = typeof crypto?.randomUUID === 'function' ? crypto.randomUUID().replaceAll('-', '') : `${Date.now().toString(36)}${Math.random().toString(36).slice(2)}`; return `horse-flight-${action}-${random}` }
function messageForError(reason: unknown, fallback: string): string { return reason instanceof Error && reason.message.trim().length > 0 ? reason.message : fallback }
function isTableUnavailable(reason: unknown): boolean { return reason instanceof HorseFlightGatewayError && reason.code === 'horse-flight-disabled' }
function spriteFrameFor(game: HorseState, runningFrame: number): HorseSpriteFrame {
  if (game.phase !== 'running') return { sheet: horseJumpSpriteSheet, index: 6 }
  if (game.grounded && game.slideTicksRemaining > 0) return { sheet: horseDashSpriteSheet, index: Math.floor(game.tick / 2) % 8 }
  if (!game.grounded) return { sheet: horseJumpSpriteSheet, index: jumpFrameFor(game.velocity) }
  return { sheet: horseRunSpriteSheet, index: runningFrame }
}

function jumpFrameFor(velocity: number): number {
  if (velocity < -11) return 1
  if (velocity < -8) return 2
  if (velocity < -4) return 3
  if (velocity < 1) return 4
  if (velocity < 6) return 5
  return 6
}

function obstacleSpriteFrameFor(spriteSheet: ObstacleSpriteSheet, tick: number, obstacleId: number, startedAt?: number) {
  const frameCount = spriteSheet.columns * spriteSheet.rows
  const animationTick = startedAt === undefined
    ? tick + obstacleId * 2
    : Math.min((frameCount - 1) * spriteSheet.ticksPerFrame, Math.max(0, tick - startedAt))
  const frameIndex = Math.floor(animationTick / spriteSheet.ticksPerFrame) % frameCount
  const sourceFrameWidth = spriteSheet.width / spriteSheet.columns
  const sourceFrameHeight = spriteSheet.height / spriteSheet.rows
  const crop = spriteSheet.frameCrop
  return {
    x: (frameIndex % spriteSheet.columns) * sourceFrameWidth + (crop?.x ?? 0),
    y: Math.floor(frameIndex / spriteSheet.columns) * sourceFrameHeight + (crop?.y ?? 0),
    width: crop?.width ?? sourceFrameWidth,
    height: crop?.height ?? sourceFrameHeight,
    sheetWidth: spriteSheet.width,
    sheetHeight: spriteSheet.height,
  }
}
