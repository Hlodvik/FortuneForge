import { useCallback, useEffect, useRef, useState, type CSSProperties, type PointerEvent as ReactPointerEvent } from 'react'
import './asteroidsTouchControls.css'

export type AsteroidsTouchControl = 'left' | 'right' | 'thrust' | 'fire'

export type AsteroidsTouchControlsProps = Readonly<{
  disabled: boolean
  shipAngle: number
  onControlChange: (control: AsteroidsTouchControl, pressed: boolean) => void
}>

const joystickDeadZone = 12
const headingTolerance = .42

export function AsteroidsTouchControls({ disabled, shipAngle, onControlChange }: AsteroidsTouchControlsProps) {
  const joystickPointerRef = useRef<number | null>(null)
  const joystickOriginRef = useRef({ x: 0, y: 0 })
  const joystickDeltaRef = useRef({ x: 0, y: 0 })
  const activeDirectionsRef = useRef<ReadonlySet<AsteroidsTouchControl>>(new Set())
  const fireSourceRef = useRef<number | 'keyboard' | null>(null)
  const [knobOffset, setKnobOffset] = useState({ x: 0, y: 0 })
  const [firePressed, setFirePressed] = useState(false)

  const applyDirections = useCallback((next: ReadonlySet<AsteroidsTouchControl>) => {
    for (const control of activeDirectionsRef.current) if (!next.has(control)) onControlChange(control, false)
    for (const control of next) if (!activeDirectionsRef.current.has(control)) onControlChange(control, true)
    activeDirectionsRef.current = next
  }, [onControlChange])

  const releaseJoystick = useCallback(() => {
    joystickPointerRef.current = null
    joystickDeltaRef.current = { x: 0, y: 0 }
    applyDirections(new Set())
    setKnobOffset({ x: 0, y: 0 })
  }, [applyDirections])

  const releaseFire = useCallback((source?: number | 'keyboard') => {
    if (source !== undefined && fireSourceRef.current !== source) return
    if (fireSourceRef.current !== null) onControlChange('fire', false)
    fireSourceRef.current = null
    setFirePressed(false)
  }, [onControlChange])

  useEffect(() => {
    if (disabled) {
      releaseJoystick()
      releaseFire()
    }
  }, [disabled, releaseFire, releaseJoystick])

  useEffect(() => {
    if (joystickPointerRef.current === null) return
    const delta = joystickDeltaRef.current
    applyDirections(controlsForJoystickDelta(delta.x, delta.y, shipAngle))
  }, [applyDirections, shipAngle])

  useEffect(() => () => {
    for (const control of activeDirectionsRef.current) onControlChange(control, false)
    if (fireSourceRef.current !== null) onControlChange('fire', false)
  }, [onControlChange])

  const startJoystick = (event: ReactPointerEvent<HTMLDivElement>) => {
    if (disabled || joystickPointerRef.current !== null) return
    event.preventDefault()
    joystickPointerRef.current = event.pointerId
    joystickOriginRef.current = { x: event.clientX, y: event.clientY }
    joystickDeltaRef.current = { x: 0, y: 0 }
    event.currentTarget.setPointerCapture?.(event.pointerId)
    setKnobOffset({ x: 0, y: 0 })
  }

  const moveJoystick = (event: ReactPointerEvent<HTMLDivElement>) => {
    if (joystickPointerRef.current !== event.pointerId) return
    event.preventDefault()
    const deltaX = event.clientX - joystickOriginRef.current.x
    const deltaY = event.clientY - joystickOriginRef.current.y
    joystickDeltaRef.current = { x: deltaX, y: deltaY }
    const maximumTravel = Math.max(24, event.currentTarget.clientWidth * .31)
    setKnobOffset(clampJoystickOffset(deltaX, deltaY, maximumTravel))
    applyDirections(controlsForJoystickDelta(deltaX, deltaY, shipAngle))
  }

  const stopJoystick = (event: ReactPointerEvent<HTMLDivElement>) => {
    if (joystickPointerRef.current !== event.pointerId) return
    releaseJoystick()
  }

  const startFire = (event: ReactPointerEvent<HTMLButtonElement>) => {
    if (disabled || fireSourceRef.current !== null) return
    event.preventDefault()
    fireSourceRef.current = event.pointerId
    event.currentTarget.setPointerCapture?.(event.pointerId)
    onControlChange('fire', true)
    setFirePressed(true)
  }

  const knobStyle = {
    '--asteroids-stick-x': `${knobOffset.x}px`,
    '--asteroids-stick-y': `${knobOffset.y}px`,
  } as CSSProperties

  return <div className="ff-asteroids-mobile-controls" aria-label="Touch controls">
    <div
      className="ff-asteroids-joystick"
      role="group"
      aria-label="Direction control"
      aria-disabled={disabled}
      onContextMenu={event => event.preventDefault()}
      onPointerDown={startJoystick}
      onPointerMove={moveJoystick}
      onPointerUp={stopJoystick}
      onPointerCancel={stopJoystick}
      onLostPointerCapture={stopJoystick}>
      <span className="ff-asteroids-joystick-knob" style={knobStyle} aria-hidden="true" />
    </div>
    <button
      type="button"
      className={`ff-asteroids-fire-control${firePressed ? ' is-pressed' : ''}`}
      aria-label="Fire"
      disabled={disabled}
      onContextMenu={event => event.preventDefault()}
      onPointerDown={startFire}
      onPointerUp={event => releaseFire(event.pointerId)}
      onPointerCancel={event => releaseFire(event.pointerId)}
      onLostPointerCapture={event => releaseFire(event.pointerId)}
      onKeyDown={event => {
        if ((event.key === ' ' || event.key === 'Enter') && fireSourceRef.current === null) {
          event.preventDefault()
          fireSourceRef.current = 'keyboard'
          onControlChange('fire', true)
          setFirePressed(true)
        }
      }}
      onKeyUp={event => {
        if (event.key === ' ' || event.key === 'Enter') {
          event.preventDefault()
          releaseFire('keyboard')
        }
      }}>
      <span aria-hidden="true" />
    </button>
  </div>
}

export function controlsForJoystickDelta(deltaX: number, deltaY: number, shipAngle = -Math.PI / 2, deadZone = joystickDeadZone): ReadonlySet<AsteroidsTouchControl> {
  const controls = new Set<AsteroidsTouchControl>()
  if (Math.hypot(deltaX, deltaY) <= deadZone) return controls
  const desiredHeading = Math.atan2(deltaY, deltaX)
  const difference = normalizeAngle(desiredHeading - shipAngle)
  if (difference < -headingTolerance) controls.add('left')
  else if (difference > headingTolerance) controls.add('right')
  else controls.add('thrust')
  return controls
}

function normalizeAngle(angle: number): number {
  let normalized = angle
  while (normalized > Math.PI) normalized -= Math.PI * 2
  while (normalized < -Math.PI) normalized += Math.PI * 2
  return normalized
}

function clampJoystickOffset(x: number, y: number, maximumTravel: number): Readonly<{ x: number; y: number }> {
  const distance = Math.hypot(x, y)
  if (distance <= maximumTravel || distance === 0) return { x, y }
  const scale = maximumTravel / distance
  return { x: x * scale, y: y * scale }
}
