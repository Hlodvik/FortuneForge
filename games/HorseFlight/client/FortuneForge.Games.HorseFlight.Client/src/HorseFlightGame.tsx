import { useCallback, useEffect, useRef, useState, type ReactNode } from 'react'
import { HorseFlightGatewayError, type HorseFlightGateway, type HorseFlightResult, type HorseFlightStatus } from './contracts'
import { HttpHorseFlightGateway } from './httpHorseFlightGateway'
import { biomeSectionForTick, horseScreenX, startHorse, stepHorse, type HorseBiome, type HorseState, type ObstacleKind } from './horseFlightSimulation'
import { HorseBiomeScenery } from './HorseBiomeScenery'
import { HorseBiomeObstacleArt, isHorseBiomeObstacleKind } from './HorseBiomeObstacleArt'
import { CaptureHorseArtwork, CaptureThrowArtwork } from './HorseCaptureArtwork'
import { horseSpriteScale, jumpSpriteGeometry, terminalGroundBaseline, terminalSpriteGeometry } from './horseSpriteScale'
import horseDashSpriteSheet from './horse-flight-dash-sprite-sheet-v1.png?no-inline'
import horseCrashSpriteSheet from './horse-flight-crash-sprite-sheet-v2.png?no-inline'
import horseJumpSpriteSheet from './horse-flight-jump-sprite-sheet-v3.png?no-inline'
import horseRearSpriteSheet from './horse-flight-rear-sprite-sheet-v2.png?no-inline'
import horseSlipSpriteSheet from './horse-flight-slip-sprite-sheet-v2.png?no-inline'
import horseRockSkidSpriteSheet from './horse-flight-rock-skid-sprite-sheet-v1.png?no-inline'
import horseTripSpriteSheet from './horse-flight-trip-over-sprite-sheet-v1.png?no-inline'
import horseMountainBackground from './horse-flight-mountain-background-v1.png?no-inline'
import horseRunSpriteSheet from './horse-flight-run-sprite-sheet-v3.png?no-inline'
import obstacleBoulder from './obstacles/horse-flight-obstacle-boulder-v1.png?no-inline'
import obstacleFallenLog from './obstacles/horse-flight-obstacle-fallen-log-v3.png?no-inline'
import obstacleFence from './obstacles/horse-flight-obstacle-fence-v2.png?no-inline'
import obstaclePit from './obstacles/horse-flight-obstacle-pit-v1.png?no-inline'
import obstacleWell from './obstacles/horse-flight-obstacle-well-v3.png?no-inline'
import obstacleBats from './animated-obstacles/horse-flight-animated-bats-horizontal-v2.png?no-inline'
import obstacleLassoKnockdown from './animated-obstacles/horse-flight-animated-lasso-knockdown-horizontal-v1.png?no-inline'
import obstacleRollingBarrel from './animated-obstacles/horse-flight-animated-rolling-barrel-horizontal-v3.png?no-inline'
import obstacleSkeletonDog from './animated-obstacles/horse-flight-animated-skeleton-dog-horizontal-v1.png?no-inline'
import obstacleSkeletonDogKnockdown from './animated-obstacles/horse-flight-animated-skeleton-dog-knockdown-horizontal-v1.png?no-inline'
import obstacleSkeleton from './animated-obstacles/horse-flight-animated-skeleton-horizontal-v1.png?no-inline'
import './horseFlight.css'
import './horseFlightArt.css'

const defaultGateway = new HttpHorseFlightGateway()
const legacyHorseSpriteFrameWidth = 2172 / 8
const legacyHorseSpriteFrameTop = 240
const legacyHorseSpriteFrameHeight = 224
const legacyHorseSpriteSheetWidth = 2172
const legacyHorseSpriteSheetHeight = 724
const runningTrackTop = 460
const openingRunInTicks = 42
const rearingHorseCenterX = 960 * 0.08
const runningHorseArtWidth = horseSpriteScale.running.width
const runningHorseArtHeight = horseSpriteScale.running.height
const legacyHorseArtWidth = horseSpriteScale.dash.width
const legacyHorseArtHeight = horseSpriteScale.dash.height
const rearingHorseArtWidth = horseSpriteScale.rear.width
const rearingHorseArtHeight = horseSpriteScale.rear.height
const runningFrameMilliseconds = 90
const rearingFrameMilliseconds = runningFrameMilliseconds
const rearingSpriteColumns = 3
const rearingSpriteRows = 4
const rearingSpriteFrameCount = rearingSpriteColumns * rearingSpriteRows
const rearingSpriteFrameWidth = 362
const rearingSpriteFrameHeight = 362
const rearingSpriteSheetWidth = rearingSpriteFrameWidth * rearingSpriteColumns
const rearingSpriteSheetHeight = rearingSpriteFrameHeight * rearingSpriteRows
const terminalHorseArtSize = horseSpriteScale.terminal.width
const terminalHorseFrameMilliseconds = 54
const terminalHorseSpriteFrameCount = 8
const tripForwardProgress = [0, .08, .22, .42, .62, .78, .92, 1] as const
const tripArcProgress = [0, .2, .64, .92, 1, .72, .3, 0] as const
const tripGroundProgress = [0, 0, 0, .08, .24, .54, .82, 1] as const
const skidForwardProgress = [0, .08, .19, .34, .56, .76, .91, 1] as const
const skidGroundProgress = [0, .08, .2, .4, .64, .82, .94, 1] as const
// Image generation keeps each pose in a fixed atlas cell, while these small root
// offsets keep the planted hoof and body center stable throughout the transition.
const rearingFrameOffsets = [
  { x: 8, y: 0 }, { x: 13, y: 2 }, { x: 4, y: 0 },
  { x: 3, y: 6 }, { x: 0, y: 6 }, { x: 0, y: 6 },
  { x: -8, y: 30 }, { x: -2, y: 30 }, { x: 4, y: 30 },
  { x: -3, y: 68 }, { x: 3, y: 68 }, { x: -5, y: 66 },
] as const
// The new sheet is 1774×887: each column is 443 or 444 whole pixels.
// Cropping row-specific transparent headroom makes the actual horse, not the
// atlas cell, match the approved rearing pose's visible height.
const runningFrameCrops = [
  { sourceX: 0, sourceY: 108, sourceWidth: 444, sourceHeight: 300 },
  { sourceX: 444, sourceY: 108, sourceWidth: 443, sourceHeight: 300 },
  { sourceX: 887, sourceY: 108, sourceWidth: 444, sourceHeight: 300 },
  { sourceX: 1331, sourceY: 108, sourceWidth: 443, sourceHeight: 300 },
  { sourceX: 0, sourceY: 494, sourceWidth: 444, sourceHeight: 300 },
  { sourceX: 444, sourceY: 494, sourceWidth: 443, sourceHeight: 300 },
  { sourceX: 887, sourceY: 494, sourceWidth: 444, sourceHeight: 300 },
  { sourceX: 1331, sourceY: 494, sourceWidth: 443, sourceHeight: 300 },
] as const
type HorseSpriteFrame = Readonly<{
  sheet: string
  index: number
  sourceX?: number
  sourceY?: number
  sourceWidth?: number
  sourceHeight?: number
  sheetWidth?: number
  sheetHeight?: number
  artWidth?: number
  artHeight?: number
  groundOffset?: number
}>
type TerminalHorseSpriteFrame = Readonly<{ sheet: string } & ReturnType<typeof terminalSpriteGeometry>>
type ObstacleSpriteSheet = Readonly<{
  width: number
  height: number
  columns: number
  rows: number
  ticksPerFrame: number
  frameCrop?: Readonly<{ x: number; y: number; width: number; height: number }>
}>
type StaticObstacleCrop = Readonly<{ sourceWidth: number; sourceHeight: number; x: number; y: number; width: number; height: number }>
type RenderedObstacleFrame = Readonly<{
  index: number
  x: number
  y: number
  width: number
  height: number
  sheetWidth: number
  sheetHeight: number
}>
type ObstacleArtwork = Readonly<{
  source: string
  label: string
  failureMessage: string
  spriteSheet?: ObstacleSpriteSheet
  knockdownSource?: string
  knockdownSpriteSheet?: ObstacleSpriteSheet
  staticCrop?: StaticObstacleCrop
}>

export type HorseTerminalReaction = 'crash' | 'trip-over' | 'slip' | 'rock-skid' | 'bat-retreat' | 'lasso-caught' | 'net-caught' | 'fall' | 'impact'
export type HorseTerminalPhase = Exclude<HorseState['phase'], 'running'>

const obstacleArtwork: Readonly<Record<ObstacleKind, ObstacleArtwork>> = {
  crate: { source: '', label: 'tombstone', failureMessage: 'You ran into a tombstone!' },
  'crate-cluster': { source: '', label: 'tombstone cluster', failureMessage: 'You crashed into a cluster of tombstones!' },
  pit: { source: obstaclePit, label: 'sunken pit', failureMessage: 'You fell into a sunken pit!', staticCrop: { sourceWidth: 1774, sourceHeight: 887, x: 60, y: 304, width: 1654, height: 355 } },
  well: { source: obstacleWell, label: 'well', failureMessage: 'You ran into a well!', staticCrop: { sourceWidth: 112, sourceHeight: 104, x: 8, y: 12, width: 96, height: 88 } },
  fence: { source: obstacleFence, label: 'fence', failureMessage: 'You tripped over a fence!', staticCrop: { sourceWidth: 354, sourceHeight: 177, x: 76, y: 59, width: 203, height: 66 } },
  carriage: { source: obstacleBoulder, label: 'wooden obstacle', failureMessage: 'A carriage blocked your escape!', staticCrop: { sourceWidth: 1536, sourceHeight: 1024, x: 275, y: 275, width: 987, height: 518 } },
  'oil-spill': { source: '', label: 'oil slick', failureMessage: 'You slipped on an oil slick!' },
  boulder: { source: obstacleBoulder, label: 'boulder', failureMessage: 'You ran into a boulder!', staticCrop: { sourceWidth: 1536, sourceHeight: 1024, x: 275, y: 275, width: 987, height: 518 } },
  'fallen-log': { source: obstacleFallenLog, label: 'fallen log', failureMessage: 'You tripped over a fallen log!', staticCrop: { sourceWidth: 160, sourceHeight: 80, x: 4, y: 20, width: 144, height: 40 } },
  dog: {
    source: obstacleSkeletonDog,
    label: 'skeleton hound',
    failureMessage: 'A skeleton hound caught you!',
    spriteSheet: { width: 2172, height: 724, columns: 6, rows: 1, ticksPerFrame: 3, frameCrop: { x: 0, y: 180, width: 362, height: 360 } },
    knockdownSource: obstacleSkeletonDogKnockdown,
    knockdownSpriteSheet: { width: 2172, height: 724, columns: 6, rows: 1, ticksPerFrame: 2, frameCrop: { x: 0, y: 180, width: 362, height: 360 } },
  },
  'bat-flock': { source: obstacleBats, label: 'flying bats', failureMessage: 'A flock of bats scared you back!', spriteSheet: { width: 1152, height: 128, columns: 6, rows: 1, ticksPerFrame: 3 } },
  'lasso-thrower': {
    source: '',
    label: 'lasso thrower',
    failureMessage: 'A peasant caught you with a lasso!',
    knockdownSource: obstacleLassoKnockdown,
    knockdownSpriteSheet: { width: 768, height: 112, columns: 6, rows: 1, ticksPerFrame: 2 },
  },
  'net-tower': { source: '', label: 'net tower', failureMessage: 'You were caught in the tower’s net!' },
  skeleton: { source: obstacleSkeleton, label: 'shambling skeleton', failureMessage: 'A skeleton blocked your escape!', spriteSheet: { width: 768, height: 112, columns: 6, rows: 1, ticksPerFrame: 3 } },
  'rolling-barrel': { source: obstacleRollingBarrel, label: 'rolling barrel', failureMessage: 'A rolling barrel knocked you down!', spriteSheet: { width: 768, height: 128, columns: 4, rows: 1, ticksPerFrame: 3 } },
  'market-cart': { source: '', label: 'market cart', failureMessage: 'You crashed into a market cart!' },
  'hanging-sign': { source: '', label: 'hanging sign', failureMessage: 'You struck a hanging sign!' },
  'thorn-bramble': { source: '', label: 'thorn bramble', failureMessage: 'You were caught in thorn brambles!' },
  'spike-barricade': { source: '', label: 'spike barricade', failureMessage: 'You hit a spike barricade!' },
  cactus: { source: '', label: 'cactus', failureMessage: 'You ran into a cactus!' },
  'storm-cloud': { source: '', label: 'storm cloud', failureMessage: 'A storm cloud struck you!' },
}

type GroundPalette = Readonly<{ earth: string; seam: string; stone: string; turf: string; turfLight: string; turfDark: string }>
const groundPalettes: Readonly<Record<HorseBiome, GroundPalette>> = {
  mountain: { earth: '#181a27', seam: '#3b4051', stone: '#282c3d', turf: '#609b14', turfLight: '#86c828', turfDark: '#426f12' },
  'castle-town': { earth: '#48454a', seam: '#797379', stone: '#5e5860', turf: '#8e867d', turfLight: '#b4a796', turfDark: '#625e60' },
  'dark-forest': { earth: '#14252b', seam: '#304747', stone: '#21363b', turf: '#2f513a', turfLight: '#55754a', turfDark: '#203e33' },
  battleground: { earth: '#302b32', seam: '#64534b', stone: '#443b3e', turf: '#655b4a', turfLight: '#8a795c', turfDark: '#4b453c' },
  haunted: { earth: '#1d1b29', seam: '#49465d', stone: '#302c40', turf: '#3d4a42', turfLight: '#657866', turfDark: '#29372e' },
  desert: { earth: '#805a4d', seam: '#b68a62', stone: '#9b7155', turf: '#c59a65', turfLight: '#e4bc7c', turfDark: '#a8784f' },
  sky: { earth: '#586a75', seam: '#a2afb2', stone: '#718892', turf: '#a9b8b9', turfLight: '#e0e5dc', turfDark: '#809ca4' },
}

function blendColor(first: string, second: string, fraction: number): string {
  const channel = (offset: number) => {
    const start = Number.parseInt(first.slice(offset, offset + 2), 16)
    const end = Number.parseInt(second.slice(offset, offset + 2), 16)
    return Math.round(start + (end - start) * fraction).toString(16).padStart(2, '0')
  }
  return `#${channel(1)}${channel(3)}${channel(5)}`
}

function paletteForSection(section: ReturnType<typeof biomeSectionForTick>): GroundPalette {
  if (section.phase === 'main') return groundPalettes[section.biome]
  const first = groundPalettes[section.from]
  const second = groundPalettes[section.to]
  return {
    earth: blendColor(first.earth, second.earth, section.blend),
    seam: blendColor(first.seam, second.seam, section.blend),
    stone: blendColor(first.stone, second.stone, section.blend),
    turf: blendColor(first.turf, second.turf, section.blend),
    turfLight: blendColor(first.turfLight, second.turfLight, section.blend),
    turfDark: blendColor(first.turfDark, second.turfDark, section.blend),
  }
}

export type HorseFlightEndScreenState = Readonly<{
  error: string | null
  isSaving: boolean
  phase: HorseTerminalPhase
  result: HorseFlightResult | null
  retrySave: () => void
  score: number
  terminalMessage: string
  terminalObstacleKind: ObstacleKind | null
}>

export type HorseFlightLaunchScreenState = Readonly<{
  available: boolean
  connecting: boolean
  error: string | null
  retryConnection: () => void
}>

export type HorseFlightGameProps = Readonly<{
  autoStart?: boolean
  debugFreePlay?: boolean
  gateway?: HorseFlightGateway
  onResult?: (result: HorseFlightResult) => void
  playerId?: string
  renderEndScreen?: (state: HorseFlightEndScreenState) => ReactNode
  renderLaunchScreen?: (state: HorseFlightLaunchScreenState) => ReactNode
}>

export function HorseFlightGame({ autoStart = false, debugFreePlay = false, gateway = defaultGateway, onResult, renderEndScreen, renderLaunchScreen }: HorseFlightGameProps) {
  const [status, setStatus] = useState<HorseFlightStatus | null>(null)
  const [game, setGame] = useState<HorseState | null>(null)
  const [result, setResult] = useState<HorseFlightResult | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [tableUnavailable, setTableUnavailable] = useState(false)
  const [statusLoadAttempt, setStatusLoadAttempt] = useState(0)
  const [openingPose, setOpeningPose] = useState(false)
  const [rearingFrame, setRearingFrame] = useState(0)
  const [runningFrame, setRunningFrame] = useState(0)
  const [terminalHorseFrame, setTerminalHorseFrame] = useState(0)
  const [paused, setPaused] = useState(false)
  const [godMode, setGodMode] = useState(false)
  const [fastMode, setFastMode] = useState(false)
  const gameRef = useRef<HorseState | null>(null)
  const runId = useRef<string | null>(null)
  const queuedJump = useRef(false)
  const queuedRightClick = useRef(false)
  const dashHeld = useRef(false)
  const fastModeHeld = useRef(false)
  const godModeEnabled = useRef(false)
  const usedDebugMode = useRef(false)
  const jumps = useRef<number[]>([])
  const rightClicks = useRef<number[]>([])
  const submitted = useRef(false)
  const field = useRef<HTMLElement | null>(null)
  const startRequestKey = useRef<string | null>(null)
  const completionRequestKey = useRef<string | null>(null)
  const autoStartRequested = useRef(false)
  // The game package is prebuilt for the app, so its own Vite DEV flag is
  // always false even when the consuming app runs locally. The route supplies
  // the DEV-gated prop; the loopback check keeps this test hook local-only.
  const isLocalHost = typeof window !== 'undefined' && ['localhost', '127.0.0.1', '[::1]'].includes(window.location.hostname)
  const canDebugFreePlay = isLocalHost && debugFreePlay && status?.mode === 'free'

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
    fastModeHeld.current = false
    godModeEnabled.current = false
    usedDebugMode.current = false
    setGodMode(false)
    setFastMode(false)
    jumps.current = []
    rightClicks.current = []
    setPaused(false)
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
    if (!autoStart || autoStartRequested.current || !status?.available || game !== null) return
    autoStartRequested.current = true
    void start()
  }, [autoStart, game, start, status?.available])

  useEffect(() => {
    if (game?.phase !== 'running') {
      setOpeningPose(false)
      setRearingFrame(0)
      return undefined
    }

    setOpeningPose(true)
    setRearingFrame(0)
    const startedAt = Date.now()
    let timer = 0
    const advanceOpening = () => {
      const elapsed = Date.now() - startedAt
      const nextFrame = Math.floor(elapsed / rearingFrameMilliseconds)
      if (nextFrame >= rearingSpriteFrameCount) {
        setOpeningPose(false)
        setRearingFrame(0)
        return
      }
      setRearingFrame(nextFrame)
      const nextFrameAt = (nextFrame + 1) * rearingFrameMilliseconds
      timer = window.setTimeout(advanceOpening, Math.max(1, nextFrameAt - elapsed))
    }
    timer = window.setTimeout(advanceOpening, rearingFrameMilliseconds)
    return () => window.clearTimeout(timer)
  }, [game?.phase])

  useEffect(() => {
    const releaseHeldRightButton = (event: MouseEvent | PointerEvent) => {
      if (event.button !== 2 && (event.buttons & 2) !== 0) return
      dashHeld.current = false
      fastModeHeld.current = false
      setFastMode(false)
    }
    const releaseAll = () => {
      dashHeld.current = false
      fastModeHeld.current = false
      setFastMode(false)
    }
    const releaseOnHidden = () => { if (document.hidden) releaseAll() }
    window.addEventListener('mouseup', releaseHeldRightButton)
    window.addEventListener('pointerup', releaseHeldRightButton)
    window.addEventListener('pointercancel', releaseAll)
    window.addEventListener('blur', releaseAll)
    document.addEventListener('visibilitychange', releaseOnHidden)
    return () => {
      window.removeEventListener('mouseup', releaseHeldRightButton)
      window.removeEventListener('pointerup', releaseHeldRightButton)
      window.removeEventListener('pointercancel', releaseAll)
      window.removeEventListener('blur', releaseAll)
      document.removeEventListener('visibilitychange', releaseOnHidden)
    }
  }, [])

  useEffect(() => {
    if (game?.phase === 'running' && !paused) return
    fastModeHeld.current = false
    setFastMode(false)
  }, [game?.phase, paused])

  useEffect(() => {
    if (!canDebugFreePlay || game?.phase !== 'running') return undefined
    const toggleGodMode = (event: KeyboardEvent) => {
      if (event.code !== 'KeyG' || !event.ctrlKey || !event.altKey || event.repeat) return
      event.preventDefault()
      godModeEnabled.current = !godModeEnabled.current
      if (godModeEnabled.current) {
        usedDebugMode.current = true
        if (dashHeld.current) {
          dashHeld.current = false
          queuedRightClick.current = false
          fastModeHeld.current = true
          setFastMode(true)
        }
      } else if (fastModeHeld.current) {
        fastModeHeld.current = false
        setFastMode(false)
        dashHeld.current = true
        queuedRightClick.current = true
      }
      setGodMode(godModeEnabled.current)
    }
    window.addEventListener('keydown', toggleGodMode)
    return () => window.removeEventListener('keydown', toggleGodMode)
  }, [canDebugFreePlay, game?.phase])

  useEffect(() => {
    if (openingPose || game?.phase !== 'running' || paused) {
      setRunningFrame(0)
      return undefined
    }

    const timer = window.setInterval(
      () => setRunningFrame(frame => (frame + 1) % 8),
      runningFrameMilliseconds)
    return () => window.clearInterval(timer)
  }, [game?.phase, openingPose, paused])

  const complete = useCallback((terminal: HorseState) => {
    if (terminal.phase === 'running') return
    const currentRunId = runId.current
    if (submitted.current || currentRunId === null) return
    submitted.current = true
    if (canDebugFreePlay && usedDebugMode.current) {
      // Debugged runs are local-only: the replay no longer matches the server's
      // authoritative simulation and must never reach a leaderboard.
      setResult({
        runId: `horse-flight-debug-${currentRunId}`,
        balance: status?.balance ?? 0,
        score: terminal.score,
        phase: terminal.phase,
        totalTicks: terminal.tick,
        jumpTicks: [...jumps.current],
        rightClickTicks: [...rightClicks.current],
      })
      return
    }
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
      onResult?.(response)
    }).catch(reason => {
      submitted.current = false
      setError(messageForError(reason, 'Horse Flight score could not be recorded.'))
    })
  }, [canDebugFreePlay, gateway, onResult, status?.balance])

  useEffect(() => {
    if (!status || !game || game.phase !== 'running' || openingPose || paused) return undefined
    const timer = window.setInterval(() => {
      const current = gameRef.current
      if (!current || current.phase !== 'running') return
      const wantsDash = dashHeld.current || queuedRightClick.current
      const debug = canDebugFreePlay
        ? { godMode: godModeEnabled.current, speedMultiplier: fastModeHeld.current ? 3 as const : undefined }
        : undefined
      const next = stepHorse(current, queuedJump.current, wantsDash, debug)
      queuedJump.current = false
      queuedRightClick.current = false
      if (next.jumped) jumps.current.push(current.tick)
      if (next.slid || next.fastFell) rightClicks.current.push(current.tick)
      accept(next.state)
      if (next.state.phase !== 'running') complete(next.state)
    }, status.tickMilliseconds)
    return () => window.clearInterval(timer)
  }, [accept, canDebugFreePlay, complete, game?.phase, openingPose, paused, status])

  useEffect(() => {
    if (game?.phase !== 'running') return
    let frame = 0
    let prior = ''
    const poll = () => {
      const pad = navigator.getGamepads?.()[0]
      const jumpPressed = Boolean(pad?.buttons[0]?.pressed)
      const dashPressed = Boolean(pad?.buttons[1]?.pressed || pad?.buttons[7]?.pressed)
      const pausePressed = Boolean(pad?.buttons[9]?.pressed)
      const current = `${jumpPressed}:${dashPressed}:${pausePressed}`
      if (current !== prior) {
        if (jumpPressed) jump()
        if (dashPressed) beginDash(); else endDash()
        if (pausePressed) setPaused(value => !value)
        prior = current
      }
      frame = window.requestAnimationFrame(poll)
    }
    frame = window.requestAnimationFrame(poll)
    return () => window.cancelAnimationFrame(frame)
  })

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

  const beginFastMode = () => {
    if (!canDebugFreePlay || !godModeEnabled.current || openingPose || gameRef.current?.phase !== 'running') return
    fastModeHeld.current = true
    usedDebugMode.current = true
    setFastMode(true)
  }

  const endFastMode = () => {
    fastModeHeld.current = false
    setFastMode(false)
  }

  const retryStatus = () => {
    setError(null)
    setTableUnavailable(false)
    setStatusLoadAttempt(attempt => attempt + 1)
  }
  const terminalReaction = horseFlightTerminalReaction(game?.phase ?? null, game?.terminalObstacleKind ?? null, game?.terminalRockLanding ?? false)
  useEffect(() => {
    if (terminalReaction !== 'crash' && terminalReaction !== 'trip-over' && terminalReaction !== 'slip' && terminalReaction !== 'rock-skid' && terminalReaction !== 'impact' && terminalReaction !== 'lasso-caught' && terminalReaction !== 'net-caught') {
      setTerminalHorseFrame(0)
      return undefined
    }

    let frame = 0
    setTerminalHorseFrame(frame)
    const timer = window.setInterval(() => {
      frame++
      setTerminalHorseFrame(Math.min(frame, terminalHorseSpriteFrameCount - 1))
      if (frame >= terminalHorseSpriteFrameCount - 1) window.clearInterval(timer)
    }, terminalHorseFrameMilliseconds)
    return () => window.clearInterval(timer)
  }, [terminalReaction])
  const terminalMessage = horseFlightTerminalMessage(game?.phase ?? null, game?.terminalObstacleKind ?? null)
  const terminalRearFrame = terminalReaction === 'bat-retreat' ? 8 : null
  const displayedRearFrame = openingPose ? rearingFrame : terminalRearFrame
  const terminalHorseSpriteFrame = terminalHorseSpriteFor(terminalReaction, terminalHorseFrame)
  const horseSpriteFrame = game && displayedRearFrame === null && terminalHorseSpriteFrame === null && terminalReaction !== 'lasso-caught' && terminalReaction !== 'net-caught' ? spriteFrameFor(game, runningFrame) : null
  const horseSpriteSourceX = horseSpriteFrame ? horseSpriteFrame.sourceX ?? horseSpriteFrame.index * legacyHorseSpriteFrameWidth : 0
  const horseSpriteSourceY = horseSpriteFrame?.sourceY ?? legacyHorseSpriteFrameTop
  const horseSpriteSourceWidth = horseSpriteFrame?.sourceWidth ?? legacyHorseSpriteFrameWidth
  const horseSpriteSourceHeight = horseSpriteFrame?.sourceHeight ?? legacyHorseSpriteFrameHeight
  const horseSpriteSheetWidth = horseSpriteFrame?.sheetWidth ?? legacyHorseSpriteSheetWidth
  const horseSpriteSheetHeight = horseSpriteFrame?.sheetHeight ?? legacyHorseSpriteSheetHeight
  const horseSpriteArtWidth = horseSpriteFrame?.artWidth ?? legacyHorseArtWidth
  const horseSpriteArtHeight = horseSpriteFrame?.artHeight ?? legacyHorseArtHeight
  const runInProgress = !openingPose && game?.phase === 'running'
  const sceneMoving = runInProgress && !paused
  const runInProgressRatio = runInProgress ? Math.min(game.tick / openingRunInTicks, 1) : 1
  const horseCenterX = openingPose
    ? rearingHorseCenterX
    : rearingHorseCenterX + ((horseScreenX - rearingHorseCenterX) * runInProgressRatio)
  const terminalHorsePosition = terminalHorseSpriteFrame && game
    ? terminalReaction === 'trip-over'
      ? tripHorsePosition(game, horseCenterX, terminalHorseFrame)
      : terminalReaction === 'rock-skid'
        ? skidHorsePosition(game, horseCenterX, terminalHorseFrame)
      : groundImpactHorsePosition(game, horseCenterX, terminalHorseFrame)
    : null
  const rearingFrameColumn = (displayedRearFrame ?? 0) % rearingSpriteColumns
  const rearingFrameRow = Math.floor((displayedRearFrame ?? 0) / rearingSpriteColumns)
  const rearingFrameOffset = rearingFrameOffsets[displayedRearFrame ?? 0] ?? rearingFrameOffsets[0]
  const biomeSection = biomeSectionForTick(game?.tick ?? 0, game?.rulesVersion)
  const activeBiome = biomeSection.biome
  const groundPalette = paletteForSection(biomeSection)
  const activeLasso = game ? activeCaptureObstacle(game, 'lasso-thrower') : null
  const activeNetTower = game ? activeCaptureObstacle(game, 'net-tower') : null

  return <main className="ff-horse-flight" data-active-biome={activeBiome}>
    {!game && <section className="ff-horse-flight__lobby">
      <div className="ff-horse-flight__lobby-scene" style={{ backgroundImage: `linear-gradient(180deg, rgb(5 11 23 / 36%) 0%, rgb(5 11 23 / 58%) 54%, rgb(5 11 23 / 86%) 100%), url(${horseMountainBackground})` }}>
        {renderLaunchScreen
          ? renderLaunchScreen({
            available: status?.available === true,
            connecting: status === null && error === null,
            error,
            retryConnection: retryStatus,
          })
          : <DefaultLaunchScreen
            available={status?.available === true}
            connecting={status === null && error === null}
            error={error}
            onRetry={retryStatus}
            onStart={() => void start()}
            unavailable={tableUnavailable}
          />}
      </div>
    </section>}
    {game && <>
      <div className="ff-horse-flight__stats"><span>Score <b>{game.score}</b></span><span>Level <b>{1 + Math.floor(game.score / 500)}</b></span><span>Jumps <b>{game.jumpsRemaining}</b></span><span>Dash <b>{game.slideTicksRemaining > 0 ? 'On' : 'Ready'}</b></span>{canDebugFreePlay && godMode && <span className="ff-horse-flight__debug-mode">God mode{fastMode ? ' · 3×' : ''}</span>}</div>
      <section className="ff-horse-flight__field" data-scene-moving={sceneMoving} data-terminal-reaction={terminalReaction ?? undefined} data-debug-god-mode={canDebugFreePlay && godMode || undefined} ref={field} tabIndex={0} aria-label="Horse Flight playfield" onMouseDown={event => { if (event.button === 0) { field.current?.focus(); jump() } else if (event.button === 2) { event.preventDefault(); field.current?.focus(); if (canDebugFreePlay && godModeEnabled.current) beginFastMode(); else beginDash() } }} onMouseUp={event => { if (event.button === 2) { endDash(); endFastMode() } }} onMouseLeave={() => { endDash(); endFastMode() }} onContextMenu={event => event.preventDefault()} onKeyDown={event => { if (event.code === 'Space' && !event.repeat) { event.preventDefault(); jump() } else if ((event.code === 'KeyP' || event.code === 'Escape') && !event.repeat) { event.preventDefault(); setPaused(value => !value) } }}>
        <svg viewBox="0 0 960 540" role="img" aria-label="Horse and platforms" data-active-biome={activeBiome} data-terminal-reaction={terminalReaction ?? undefined}>
          <defs>
            <pattern id="horse-flight-earth" width="32" height="32" patternUnits="userSpaceOnUse">
              <rect width="32" height="32" fill={groundPalette.earth} />
              <rect width="32" height="3" fill={groundPalette.seam} />
              <rect y="3" width="32" height="4" fill={groundPalette.stone} />
              <rect x="2" y="9" width="8" height="2" fill={groundPalette.seam} /><rect x="17" y="8" width="11" height="2" fill={groundPalette.stone} />
              <rect x="8" y="15" width="6" height="2" fill={groundPalette.seam} /><rect x="24" y="18" width="5" height="2" fill={groundPalette.stone} />
              <rect x="1" y="24" width="12" height="2" fill={groundPalette.stone} /><rect x="17" y="27" width="9" height="2" fill={groundPalette.seam} />
            </pattern>
            <pattern id="horse-flight-grass" width="28" height="14" patternUnits="userSpaceOnUse">
              <rect width="28" height="14" fill={groundPalette.turf} />
              <rect y="3" width="28" height="4" fill={groundPalette.turfLight} /><rect y="8" width="28" height="6" fill={groundPalette.turfDark} />
              <rect x="1" width="2" height="5" fill={groundPalette.turfLight} /><rect x="7" y="1" width="2" height="4" fill={groundPalette.turfLight} />
              <rect x="15" width="2" height="5" fill={groundPalette.turfLight} /><rect x="23" y="1" width="2" height="4" fill={groundPalette.turfLight} />
              <rect x="4" y="7" width="6" height="2" fill={groundPalette.turf} /><rect x="18" y="7" width="7" height="2" fill={groundPalette.turf} />
            </pattern>
            <linearGradient id="horse-flight-coat" x2="0" y2="1"><stop stopColor="#f3c76a" /><stop offset="1" stopColor="#9f572a" /></linearGradient>
            <clipPath id="horse-flight-scene"><rect width="960" height={runningTrackTop} /></clipPath>
          </defs>
          <g clipPath="url(#horse-flight-scene)" aria-hidden="true">
            <HorseBiomeScenery section={biomeSection} layer="background" distance={game.distance} tick={game.tick} />
            <HorseBiomeScenery section={biomeSection} layer="midground" distance={game.distance} tick={game.tick} />
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
          <g clipPath="url(#horse-flight-scene)"><HorseBiomeScenery section={biomeSection} layer="foreground" distance={game.distance} tick={game.tick} /></g>
          {game.obstacles.map(obstacle => {
            const platform = game.platforms.find(item => item.id === obstacle.platformId)
            const artwork = obstacleArtwork[obstacle.kind]
            const isKnockedDown = obstacle.knockedDownAt !== undefined && artwork.knockdownSpriteSheet !== undefined
            const spriteSheet = isKnockedDown ? artwork.knockdownSpriteSheet : artwork.spriteSheet
            const spriteSource = isKnockedDown ? artwork.knockdownSource! : artwork.source
            const spriteFrame = spriteSheet ? obstacleSpriteFrameFor(spriteSheet, game.tick, obstacle.id, obstacle.knockedDownAt) : null
            const staticCrop = artwork.staticCrop
            return platform && <g key={obstacle.id} className="ff-horse-flight__obstacle" data-obstacle-kind={obstacle.kind} data-clearance={obstacle.elevation > 0 ? 'high' : 'ground'} data-knocked-down={isKnockedDown || undefined} data-attack-stage={captureAttackStage(obstacle.kind, obstacle.x, terminalReaction)} aria-label={`${obstacle.elevation > 0 ? 'High' : 'Ground'} hazard: ${artwork.label}`} transform={`translate(${obstacle.x} ${platform.y - obstacle.elevation - obstacle.height})`}>
              {isHorseBiomeObstacleKind(obstacle.kind)
                ? <HorseBiomeObstacleArt kind={obstacle.kind} width={obstacle.width} height={obstacle.height} />
                : obstacle.kind === 'crate' || obstacle.kind === 'crate-cluster'
                ? <TombstoneObstacle width={obstacle.width} height={obstacle.height} cluster={obstacle.kind === 'crate-cluster'} />
                : obstacle.kind === 'oil-spill'
                  ? <GreenGooObstacle width={obstacle.width} height={obstacle.height} />
                : !isKnockedDown && (obstacle.kind === 'lasso-thrower' || obstacle.kind === 'net-tower')
                  ? <CaptureThrowArtwork obstacle={obstacle} platformY={platform.y} animationFrame={runningFrame} caught={obstacle.kind === 'lasso-thrower' ? terminalReaction === 'lasso-caught' && activeLasso?.id === obstacle.id : terminalReaction === 'net-caught' && activeNetTower?.id === obstacle.id} />
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
          <g className="ff-horse-flight__horse-actor" data-terminal-reaction={terminalReaction ?? undefined} aria-hidden="true">
            {displayedRearFrame !== null && <svg className="ff-horse-flight__horse-art" x={horseCenterX - (rearingHorseArtWidth / 2)} y={game.horseY - rearingHorseArtHeight + 1} width={rearingHorseArtWidth} height={rearingHorseArtHeight} viewBox={`0 0 ${rearingSpriteFrameWidth} ${rearingSpriteFrameHeight}`} preserveAspectRatio="none" overflow="hidden" imageRendering="pixelated" data-sprite-frame={displayedRearFrame}>
              <defs><clipPath id="horse-flight-rear-frame" clipPathUnits="userSpaceOnUse"><rect width={rearingSpriteFrameWidth} height={rearingSpriteFrameHeight} /></clipPath></defs>
              <g transform={`translate(${rearingFrameOffset.x} ${rearingFrameOffset.y})`}>
                <g clipPath="url(#horse-flight-rear-frame)"><image href={horseRearSpriteSheet} x={-rearingFrameColumn * rearingSpriteFrameWidth} y={-rearingFrameRow * rearingSpriteFrameHeight} width={rearingSpriteSheetWidth} height={rearingSpriteSheetHeight} preserveAspectRatio="none" /></g>
              </g>
            </svg>}
            {terminalHorseSpriteFrame && <svg className="ff-horse-flight__horse-art ff-horse-flight__horse-reaction-art" x={terminalHorsePosition?.x ?? horseCenterX - (terminalHorseArtSize / 2)} y={terminalHorsePosition?.y ?? game.horseY - terminalHorseArtSize + 1} width={terminalHorseArtSize} height={terminalHorseArtSize} viewBox={`0 0 ${terminalHorseSpriteFrame.sourceWidth} ${terminalHorseSpriteFrame.sourceHeight}`} preserveAspectRatio="none" overflow="hidden" imageRendering="pixelated" data-terminal-horse-sprite={terminalReaction ?? undefined} data-sprite-frame={terminalHorseSpriteFrame.index}>
              <defs><clipPath id="horse-flight-terminal-frame" clipPathUnits="userSpaceOnUse"><rect x={terminalReaction === 'rock-skid' && terminalHorseSpriteFrame.index === 7 ? 10 : 0} y={terminalReaction === 'trip-over' && terminalHorseSpriteFrame.index === 7 ? 220 : 0} width={terminalHorseSpriteFrame.sourceWidth} height={terminalHorseSpriteFrame.sourceHeight} /></clipPath></defs>
              <g transform={`translate(0 ${terminalHorseSpriteFrame.groundOffset})`}>
                <g clipPath="url(#horse-flight-terminal-frame)"><image href={terminalHorseSpriteFrame.sheet} x={-terminalHorseSpriteFrame.sourceX} y={-terminalHorseSpriteFrame.sourceY} width={terminalHorseSpriteFrame.sheetWidth} height={terminalHorseSpriteFrame.sheetHeight} preserveAspectRatio="none" /></g>
              </g>
            </svg>}
            {terminalReaction === 'lasso-caught' && activeLasso && <CaptureHorseArtwork kind="lasso-thrower" horseX={horseCenterX} horseY={game.horseY} platformY={platformYFor(game, activeLasso.platformId)} reactionFrame={terminalHorseFrame} />}
            {terminalReaction === 'net-caught' && activeNetTower && <CaptureHorseArtwork kind="net-tower" horseX={horseCenterX} horseY={game.horseY} platformY={platformYFor(game, activeNetTower.platformId)} reactionFrame={terminalHorseFrame} />}
            {horseSpriteFrame && <svg className="ff-horse-flight__horse-art" x={horseCenterX - (horseSpriteArtWidth / 2)} y={game.horseY - horseSpriteArtHeight + 1} width={horseSpriteArtWidth} height={horseSpriteArtHeight} viewBox={`0 0 ${horseSpriteSourceWidth} ${horseSpriteSourceHeight}`} preserveAspectRatio="none" overflow="hidden" imageRendering="pixelated" data-sprite-frame={horseSpriteFrame.index}>
              <defs><clipPath id="horse-flight-running-frame" clipPathUnits="userSpaceOnUse"><rect width={horseSpriteSourceWidth} height={horseSpriteSourceHeight} /></clipPath></defs>
              <g transform={`translate(0 ${horseSpriteFrame.groundOffset ?? 0})`}>
                <g clipPath="url(#horse-flight-running-frame)"><image href={horseSpriteFrame.sheet} x={-horseSpriteSourceX} y={-horseSpriteSourceY} width={horseSpriteSheetWidth} height={horseSpriteSheetHeight} preserveAspectRatio="none" /></g>
              </g>
            </svg>}
          </g>
          {terminalReaction && <TerminalReactionEffect reaction={terminalReaction} x={horseCenterX} y={game.horseY} />}
        </svg>
        {paused && <div className="ff-horse-flight__over" role="status"><strong>Run paused</strong><span>Your replay clock is stopped too.</span><button type="button" onClick={() => setPaused(false)}>Resume run</button></div>}
        {game.phase !== 'running' && <div className="ff-horse-flight__over" data-terminal-overlay data-terminal-reaction={terminalReaction ?? undefined} aria-live="polite">
          {renderEndScreen
            ? renderEndScreen({
              error,
              isSaving: result === null && error === null,
              phase: game.phase,
              result,
              retrySave: () => complete(game),
              score: result?.score ?? game.score,
              terminalMessage,
              terminalObstacleKind: game.terminalObstacleKind,
            })
            : <>
              <strong>{terminalMessage}</strong>
              <strong>Score {result?.score ?? game.score}</strong>
              {!result && !error && <span>Saving score…</span>}
              {!result && error && <button onClick={() => complete(game)} type="button">Try saving again</button>}
              {result && <button onClick={() => void start()} type="button">Start fresh run</button>}
            </>}
        </div>}
        {game.phase === 'running' && <div className="ff-horse-flight__touch-controls" aria-label="Horse controls"><button type="button" onPointerDown={event => { event.stopPropagation(); jump() }}>Jump <span>ground hazard</span></button><button type="button" onPointerDown={event => { event.stopPropagation(); beginDash() }} onPointerUp={endDash} onPointerCancel={endDash}>Dash <span>high hazard</span></button><button type="button" onClick={event => { event.stopPropagation(); setPaused(value => !value) }}>{paused ? 'Resume' : 'Pause'}</button></div>}
      </section>
    </>}
  </main>
}

function DefaultLaunchScreen({
  available,
  connecting,
  error,
  onRetry,
  onStart,
  unavailable,
}: Readonly<{
  available: boolean
  connecting: boolean
  error: string | null
  onRetry: () => void
  onStart: () => void
  unavailable: boolean
}>) {
  return <div className="ff-horse-flight__default-launch">
    <h1>HORSE FLIGHT</h1>
    <p>Ready to run?</p>
    {error && <p className="ff-horse-flight__error" role="alert">{error}</p>}
    {error && !unavailable && <button className="ff-horse-flight__start" onClick={onRetry} type="button">Retry connection</button>}
    <button className="ff-horse-flight__start" disabled={!available} onClick={onStart} type="button">
      {available ? 'Start run' : unavailable ? 'Run unavailable' : connecting ? 'Connecting…' : 'Start unavailable'}
    </button>
  </div>
}





function TerminalReactionEffect({ reaction, x, y }: Readonly<{ reaction: HorseTerminalReaction; x: number; y: number }>) {
  if (reaction === 'crash' || reaction === 'trip-over' || reaction === 'impact') return <g className="ff-horse-flight__impact-burst" data-reaction={reaction} transform={`translate(${x + 25} ${y - 12})`} aria-hidden="true">
    <path d="m0-28 7 18 18-9-10 17 22 6-22 5 9 19-18-10-7 20-5-20-20 9 11-18-23-6 23-5-10-19 19 10Z" />
    <circle cx="-20" cy="18" r="9" /><circle cx="-2" cy="23" r="12" /><circle cx="18" cy="20" r="7" />
  </g>
  if (reaction === 'slip') return <g className="ff-horse-flight__slip-splash" transform={`translate(${x} ${y - 1})`} aria-hidden="true">
    <ellipse rx="39" ry="8" /><path d="M-31-4-45-18M-9-7-15-25M17-6 27-22M34-2 49-12" />
  </g>
  if (reaction === 'bat-retreat') return <g className="ff-horse-flight__retreat-lines" transform={`translate(${x - 43} ${y - 61})`} aria-hidden="true">
    <path d="M0 0-28-7M4 13-35 14M8 25-24 35" />
  </g>
  if (reaction === 'fall') return <g className="ff-horse-flight__fall-lines" transform={`translate(${x - 32} ${y - 50})`} aria-hidden="true"><path d="M0 0h-34M8 14h-28M3 29h-40" /></g>
  return null
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
  if (game.phase !== 'running') return jumpHorseSpriteFrame(6)
  if (game.grounded && game.slideTicksRemaining > 0) return { sheet: horseDashSpriteSheet, index: Math.floor(game.tick / 2) % 8 }
  if (!game.grounded) return jumpHorseSpriteFrame(jumpFrameFor(game.velocity))
  const crop = runningFrameCrops[runningFrame] ?? runningFrameCrops[0]
  return { sheet: horseRunSpriteSheet, index: runningFrame, ...crop, sheetWidth: 1774, sheetHeight: 887, artWidth: runningHorseArtWidth, artHeight: runningHorseArtHeight }
}

function jumpHorseSpriteFrame(frame: number): HorseSpriteFrame {
  return { sheet: horseJumpSpriteSheet, ...jumpSpriteGeometry(frame), artWidth: horseSpriteScale.jump.width, artHeight: horseSpriteScale.jump.height }
}

function jumpFrameFor(velocity: number): number {
  if (velocity < -11) return 1
  if (velocity < -8) return 2
  if (velocity < -4) return 3
  if (velocity < 1) return 4
  if (velocity < 6) return 5
  return 6
}

function obstacleSpriteFrameFor(spriteSheet: ObstacleSpriteSheet, tick: number, obstacleId: number, startedAt?: number): RenderedObstacleFrame {
  const frameCount = spriteSheet.columns * spriteSheet.rows
  const animationTick = startedAt === undefined
    ? tick + obstacleId * 2
    : Math.min((frameCount - 1) * spriteSheet.ticksPerFrame, Math.max(0, tick - startedAt))
  const frameIndex = Math.floor(animationTick / spriteSheet.ticksPerFrame) % frameCount
  const sourceFrameWidth = spriteSheet.width / spriteSheet.columns
  const sourceFrameHeight = spriteSheet.height / spriteSheet.rows
  const crop = spriteSheet.frameCrop
  return {
    index: frameIndex,
    x: (frameIndex % spriteSheet.columns) * sourceFrameWidth + (crop?.x ?? 0),
    y: Math.floor(frameIndex / spriteSheet.columns) * sourceFrameHeight + (crop?.y ?? 0),
    width: crop?.width ?? sourceFrameWidth,
    height: crop?.height ?? sourceFrameHeight,
    sheetWidth: spriteSheet.width,
    sheetHeight: spriteSheet.height,
  }
}

export function horseFlightTerminalReaction(phase: HorseState['phase'] | null, obstacleKind: ObstacleKind | null, rockLanding = false): HorseTerminalReaction | null {
  if (phase === null || phase === 'running') return null
  if (phase === 'fell') return 'fall'
  if (obstacleKind === 'boulder' && rockLanding) return 'rock-skid'
  switch (obstacleKind) {
    case 'fence':
    case 'fallen-log': return 'trip-over'
    case 'market-cart':
    case 'spike-barricade':
    case 'cactus': return 'crash'
    case 'oil-spill': return 'slip'
    case 'bat-flock': return 'bat-retreat'
    case 'lasso-thrower': return 'lasso-caught'
    case 'net-tower': return 'net-caught'
    default: return 'impact'
  }
}

export function terminalHorseSpriteFor(reaction: HorseTerminalReaction | null, frame: number): TerminalHorseSpriteFrame | null {
  if (reaction !== 'crash' && reaction !== 'trip-over' && reaction !== 'slip' && reaction !== 'rock-skid' && reaction !== 'impact') return null
  const index = Math.min(terminalHorseSpriteFrameCount - 1, Math.max(0, frame))
  return {
    sheet: reaction === 'slip' ? horseSlipSpriteSheet : reaction === 'trip-over' ? horseTripSpriteSheet : reaction === 'rock-skid' ? horseRockSkidSpriteSheet : horseCrashSpriteSheet,
    ...terminalSpriteGeometry(reaction === 'slip' ? 'slip' : reaction === 'trip-over' ? 'trip' : reaction === 'rock-skid' ? 'skid' : 'crash', index),
  }
}

export function horseFlightTerminalMessage(phase: HorseState['phase'] | null, obstacleKind: ObstacleKind | null): string {
  if (phase === 'fell') return 'You missed the trail and fell!'
  if (phase !== 'obstacle-collision') return ''
  return obstacleKind === null ? 'An obstacle ended your escape!' : obstacleArtwork[obstacleKind].failureMessage
}


function captureAttackStage(kind: ObstacleKind, x: number, reaction: HorseTerminalReaction | null): 'idle' | 'prep' | 'throw' | 'caught' | undefined {
  if (kind !== 'lasso-thrower' && kind !== 'net-tower') return undefined
  if (((kind === 'lasso-thrower' && reaction === 'lasso-caught') || (kind === 'net-tower' && reaction === 'net-caught')) && Math.abs(x - horseScreenX) < 140) return 'caught'
  const distance = x - horseScreenX
  const launchDistance = kind === 'lasso-thrower' ? 200 : 260
  if (distance > launchDistance + 70) return 'idle'
  return distance > launchDistance ? 'prep' : 'throw'
}

function activeCaptureObstacle(game: HorseState, kind: 'lasso-thrower' | 'net-tower'): HorseState['obstacles'][number] | null {
  const terminal = game.phase === 'obstacle-collision' && game.terminalObstacleKind === kind
  if (terminal) {
    const struckObstacle = game.obstacles.find(obstacle => obstacle.id === game.terminalObstacleId && obstacle.kind === kind)
    if (struckObstacle) return struckObstacle
  }
  const candidates = game.obstacles
    .filter(obstacle => obstacle.kind === kind && obstacle.knockedDownAt === undefined)
    .filter(obstacle => terminal || (obstacle.x + obstacle.width > horseScreenX - 90 && obstacle.x < horseScreenX + 310))
    .sort((left, right) => Math.abs(left.x - horseScreenX) - Math.abs(right.x - horseScreenX))
  return candidates[0] ?? null
}

function terminalObstacleFor(game: HorseState, horseCenterX: number): HorseState['obstacles'][number] | null {
  const struckObstacle = game.obstacles.find(obstacle => obstacle.id === game.terminalObstacleId)
  if (struckObstacle) return struckObstacle
  return game.obstacles
    .filter(obstacle => obstacle.kind === game.terminalObstacleKind)
    .sort((left, right) => Math.abs(left.x - horseCenterX) - Math.abs(right.x - horseCenterX))[0] ?? null
}

export function groundImpactHorsePosition(game: HorseState, horseCenterX: number, frame: number): Readonly<{ x: number; y: number }> {
  const obstacle = terminalObstacleFor(game, horseCenterX)
  const groundY = obstacle ? platformYFor(game, obstacle.platformId) : game.horseY
  return {
    x: horseCenterX - terminalHorseArtSize / 2,
    y: terminalGroundBaseline(game.horseY, groundY, frame) - terminalHorseArtSize + 1,
  }
}

export function tripHorsePosition(game: HorseState, horseCenterX: number, frame: number): Readonly<{ x: number; y: number }> {
  const obstacle = terminalObstacleFor(game, horseCenterX)
  const landingTravel = obstacle
    ? Math.max(126, obstacle.x + obstacle.width - (horseCenterX - terminalHorseArtSize / 2) + 12)
    : 150
  const arcHeight = (obstacle?.height ?? 42) + 28
  const landingY = obstacle ? platformYFor(game, obstacle.platformId) : game.horseY
  const index = Math.min(terminalHorseSpriteFrameCount - 1, Math.max(0, frame))
  return {
    x: horseCenterX - terminalHorseArtSize / 2 + landingTravel * (tripForwardProgress[index] ?? 1),
    y: terminalGroundBaseline(game.horseY, landingY, index, tripGroundProgress) - terminalHorseArtSize + 1
      - arcHeight * (tripArcProgress[index] ?? 0),
  }
}

export function skidHorsePosition(game: HorseState, horseCenterX: number, frame: number): Readonly<{ x: number; y: number }> {
  const obstacle = terminalObstacleFor(game, horseCenterX)
  const initialX = horseCenterX - terminalHorseArtSize / 2
  const landingTravel = obstacle
    ? Math.max(150, obstacle.x + obstacle.width - initialX + 12)
    : 150
  const landingY = obstacle ? platformYFor(game, obstacle.platformId) : game.horseY
  const index = Math.min(terminalHorseSpriteFrameCount - 1, Math.max(0, frame))
  return {
    x: initialX + landingTravel * skidForwardProgress[index],
    y: terminalGroundBaseline(game.horseY, landingY, index, skidGroundProgress) - terminalHorseArtSize + 1,
  }
}

function platformYFor(game: HorseState, platformId: number): number {
  return game.platforms.find(platform => platform.id === platformId)?.y ?? runningTrackTop
}
