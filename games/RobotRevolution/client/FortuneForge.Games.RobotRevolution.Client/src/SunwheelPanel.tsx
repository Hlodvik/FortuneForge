import { useEffect, useRef, useState } from 'react'
import type { CSSProperties, RefObject } from 'react'
import { createPortal } from 'react-dom'
import { formatRand, slotPointsToRand } from '@fortuneforge/slot-client-core'
import type { SlotSpinFeaturePanelProps } from '@fortuneforge/slot-game-runtime/experience'
import { decodeSunwheelFeature, SUNWHEEL_GUARDIANS } from './sunwheel'
import type { EclipseMode, GuardianId, SunwheelFeature, SunwheelRing } from './sunwheel'
import { ROBOT_REVOLUTION_SYMBOL_IMAGES as artwork } from './symbols'

function GuardianPath({ ring, rows }: { ring: SunwheelRing; rows: number }) {
  const matched = new Set(ring.matchedPositions.map(cell => `${cell.reel}:${cell.row}`))
  const points = ring.path.map(cell => `${cell.reel * 20 + 10},${cell.row * (60 / rows) + 30 / rows}`).join(' ')
  return <svg className="sunwheel-path" viewBox="0 0 100 60" role="img"
    aria-label={`${ring.matches} of five ${ring.id} path positions matched`}>
    <polyline points={points} fill="none" />
    {ring.path.map(cell => <circle key={cell.reel} cx={cell.reel * 20 + 10} cy={cell.row * (60 / rows) + 30 / rows}
      r="3.4" data-matched={matched.has(`${cell.reel}:${cell.row}`)} />)}
  </svg>
}

function CalendarWheel({ feature, spinning, reducedMotion, sequenceStep }: {
  feature: SunwheelFeature | null; spinning: boolean; reducedMotion: boolean; sequenceStep: number
}) {
  return <div className="sunwheel-calendar" data-spinning={spinning || undefined}
    data-eclipse={feature?.eclipseTriggered || undefined} data-reduced-motion={reducedMotion || undefined}
    data-sequence={feature?.eclipseMode || undefined}
    aria-hidden="true">
    <svg viewBox="0 0 200 200" className="sunwheel-calendar__carving">
      <circle cx="100" cy="100" r="96" className="sunwheel-calendar__rim" />
      <circle cx="100" cy="100" r="91" className="sunwheel-calendar__ticks" />
      {SUNWHEEL_GUARDIANS.map((guardian, index) => {
        const ring = feature?.rings[index]
        const firstRotation = feature?.alignments?.[0]?.rings[index].rotationSteps ?? ring?.rotationSteps ?? index
        const rotation = (feature?.eclipseMode
          ? firstRotation + sequenceStep * (feature.eclipseMode === 'lunar' ? -1 : 1)
          : ring?.rotationSteps ?? index) * 360 / (feature?.rows ?? 4)
        const awakened = feature?.alignments?.[sequenceStep]?.awakenedGuardianIds.includes(guardian.id)
        return <g key={guardian.id} className="sunwheel-calendar__ring" data-aligned={!spinning && (awakened || ring?.aligned) || undefined}
          style={{ '--ring-turn': `${rotation}deg`, '--guardian-color': guardian.color, '--ring-delay': `${index * 120}ms` } as CSSProperties}>
          <circle cx="100" cy="100" r={78 - index * 17} />
          {Array.from({ length: 12 }, (_, tick) => <path key={tick} transform={`rotate(${tick * 30} 100 100)`}
            d={`M 96 ${25 + index * 17} h 8 l -4 7 Z`} />)}
          <circle className="sunwheel-calendar__locator" cx="100" cy={22 + index * 17} r="4" />
        </g>
      })}
      <circle cx="100" cy="100" r="24" className="sunwheel-calendar__heart" />
    </svg>
    <img className="sunwheel-calendar__sun" src={artwork.SUNSTONE} alt="" />
    <span className="sunwheel-calendar__needle" />
    <span className="sunwheel-calendar__heart-label">{feature?.heart.lit ? 'SUN LIT' : 'SUNWHEEL'}</span>
  </div>
}

function GuardianBoardPath({ feature, guardian, cabinetRef }: {
  feature: SunwheelFeature; guardian: GuardianId; cabinetRef?: RefObject<HTMLDivElement | null>
}) {
  const [geometry, setGeometry] = useState<{ target: HTMLDivElement; width: number; height: number; points: { x: number; y: number; matched: boolean }[] } | null>(null)
  const ring = feature.rings.find(candidate => candidate.id === guardian)!
  useEffect(() => {
    const cabinet = cabinetRef?.current
    if (!cabinet) return
    const measure = () => {
      const bounds = cabinet.getBoundingClientRect()
      const matched = new Set(ring.matchedPositions.map(cell => `${cell.reel}:${cell.row}`))
      const points = ring.path.map(position => {
        const cell = cabinet.querySelector<HTMLElement>(`.slot-symbol[data-reel-index="${position.reel}"][data-row-index="${position.row}"]`)
        const box = cell?.getBoundingClientRect()
        return box ? { x: box.left - bounds.left + box.width / 2, y: box.top - bounds.top + box.height / 2,
          matched: matched.has(`${position.reel}:${position.row}`) } : null
      })
      setGeometry(points.every(point => point !== null) && bounds.width > 0
        ? { target: cabinet, width: bounds.width, height: bounds.height, points } : null)
    }
    measure()
    const observer = typeof ResizeObserver === 'undefined' ? null : new ResizeObserver(measure)
    observer?.observe(cabinet)
    window.addEventListener('resize', measure)
    return () => { observer?.disconnect(); window.removeEventListener('resize', measure) }
  }, [cabinetRef, ring])
  if (!geometry) return null
  const guardianColor = SUNWHEEL_GUARDIANS.find(candidate => candidate.id === guardian)!.color
  return createPortal(<svg className="sunwheel-board-path" viewBox={`0 0 ${geometry.width} ${geometry.height}`}
    style={{ '--guardian-color': guardianColor } as CSSProperties} aria-hidden="true">
    <polyline points={geometry.points.map(point => `${point.x},${point.y}`).join(' ')} />
    {geometry.points.map((point, index) => <circle key={index} cx={point.x} cy={point.y} r="10" data-matched={point.matched} />)}
  </svg>, geometry.target)
}

function modeFromState(mode?: string): EclipseMode | null {
  return mode === 'solar-eclipse' ? 'solar' : mode === 'lunar-eclipse' ? 'lunar' : null
}

function EclipseCarvings({ cabinetRef, mode, awakened, totality, reducedMotion }: {
  cabinetRef?: RefObject<HTMLDivElement | null>; mode: EclipseMode | null; awakened: readonly GuardianId[]; totality: boolean; reducedMotion: boolean
}) {
  const cabinet = cabinetRef?.current
  if (!cabinet || !mode) return null
  return createPortal(<div className="fifth-sun-eclipse-carvings" data-eclipse-mode={mode}
    data-totality={totality || undefined} data-reduced-motion={reducedMotion || undefined} aria-hidden="true">
    {SUNWHEEL_GUARDIANS.map(guardian => <span key={guardian.id} data-awakened={awakened.includes(guardian.id) || undefined}>
      <img src={artwork[guardian.symbol]} alt="" /></span>)}
  </div>, cabinet)
}

export function SunwheelPanel({ feature: rawFeature, spinKey, isSpinning, reducedMotion, pointValueInCents, cabinetRef,
  paidMode, onPresentationBusyChange }: SlotSpinFeaturePanelProps) {
  const [selectedGuardian, setSelectedGuardian] = useState<GuardianId | null>(null)
  const settledFeature = decodeSunwheelFeature(rawFeature)
  const [sequenceStep, setSequenceStep] = useState(0)
  const [sequenceComplete, setSequenceComplete] = useState(false)
  const reportBusy = useRef(onPresentationBusyChange)
  reportBusy.current = onPresentationBusyChange
  useEffect(() => {
    const alignments = settledFeature?.alignments
    const playSequence = !isSpinning && settledFeature?.eclipseMode && alignments?.length === 3
    if (!playSequence || reducedMotion) {
      setSequenceStep(playSequence ? 2 : 0)
      setSequenceComplete(Boolean(playSequence))
      reportBusy.current?.(false)
      return
    }
    setSequenceStep(0)
    setSequenceComplete(false)
    reportBusy.current?.(true)
    const timers = [
      window.setTimeout(() => setSequenceStep(1), 1000),
      window.setTimeout(() => setSequenceStep(2), 2000),
      window.setTimeout(() => { setSequenceComplete(true); reportBusy.current?.(false) }, 3000),
    ]
    return () => { timers.forEach(timer => window.clearTimeout(timer)); reportBusy.current?.(false) }
  }, [settledFeature, spinKey, isSpinning, reducedMotion])
  const alignment = settledFeature?.eclipseMode ? settledFeature.alignments?.[sequenceStep] : undefined
  const feature = settledFeature && alignment ? {
    ...settledFeature, rings: alignment.rings, dialStep: alignment.dialStep,
    alignedRings: alignment.awakenedGuardianIds.length, alignmentMultiplier: alignment.alignmentMultiplier,
    eclipseTriggered: !isSpinning && sequenceComplete && settledFeature.eclipseTriggered,
    totalAwardPoints: settledFeature.alignments!.slice(0, sequenceStep + 1).reduce((sum, step) => sum + step.totalAwardPoints, 0)
      + (sequenceComplete ? settledFeature.totalityAwardPoints ?? 0 : 0),
  } : settledFeature
  const mode = modeFromState(paidMode?.mode) ?? (!isSpinning ? settledFeature?.eclipseMode ?? null : null)
  const awakened = !isSpinning ? alignment?.awakenedGuardianIds ?? [] : []
  const playingSequence = Boolean(settledFeature?.eclipseMode && !isSpinning && !sequenceComplete)
  const pendingMode = mode && !settledFeature?.eclipseMode
  const tracedGuardian = selectedGuardian ?? (playingSequence
    ? alignment?.rings.find(ring => ring.aligned)?.id ?? SUNWHEEL_GUARDIANS[sequenceStep].id : null)
  const hasInvalidFeature = rawFeature !== null && rawFeature !== undefined && feature === null
  const savedRound = !hasInvalidFeature && feature === null && spinKey !== null && spinKey > 0 && !isSpinning
  const award = feature ? formatRand(slotPointsToRand(feature.totalAwardPoints, pointValueInCents)) : null
  const status = isSpinning ? 'The calendar turns'
    : hasInvalidFeature ? 'Sunwheel details are unavailable'
      : savedRound ? 'Saved round settled'
        : playingSequence ? `Alignment ${sequenceStep + 1} of 3`
        : pendingMode ? `${mode === 'solar' ? 'Solar' : 'Lunar'} Eclipse awakened`
        : feature?.eclipseTriggered ? feature.contractVersion === 3 && feature.eclipseMode ? 'Totality' : 'Eclipse tribute'
        : feature && feature.alignedRings > 0 ? `${feature.alignedRings} ${feature.alignedRings === 1 ? 'guardian awakens' : 'guardians awaken'}`
          : feature ? 'The guardians rest' : 'Awaken the guardians'

  return <section className="sunwheel" data-eclipse={feature?.eclipseTriggered || undefined}
    data-eclipse-mode={mode ?? undefined} data-sequence-playing={playingSequence || undefined}
    data-active-round={feature?.isEclipseSpin || undefined} data-spinning={isSpinning || undefined}
    data-reduced-motion={reducedMotion || undefined} aria-label="Sunwheel Alignment">
    <div className="sunwheel__heading">
      <h2>Sunwheel</h2>
      {mode ? <span className="sunwheel__mode">{mode === 'solar' ? 'Solar Eclipse' : 'Lunar Eclipse'}
        {paidMode ? ` · ${paidMode.remaining} paid ${paidMode.remaining === 1 ? 'round' : 'rounds'} left` : ' · final round'}</span>
        : (savedRound || feature?.isEclipseSpin) && <span className="sunwheel__mode">{savedRound ? 'Saved bonus round' : 'Saved Eclipse spins · awards ×2'}</span>}
      {mode && <div className="sunwheel__sequence" aria-label="Eclipse alignment progress">
        {[0, 1, 2].map(step => <span key={step} data-current={!pendingMode && step === sequenceStep || undefined}
          data-complete={!pendingMode && (sequenceComplete || step < sequenceStep) || undefined}>{step + 1}</span>)}
      </div>}
    </div>
    <div className="sunwheel__body" key={spinKey ?? 'unspun'}>
      <CalendarWheel feature={feature} spinning={isSpinning} reducedMotion={reducedMotion} sequenceStep={sequenceStep} />
      <div className="sunwheel__guardians" aria-label="Guardian paths">
        {SUNWHEEL_GUARDIANS.map((guardian, index) => {
          const ring = feature?.rings[index]
          return <button type="button" key={guardian.id} className="sunwheel-guardian" data-aligned={ring?.aligned || undefined}
            data-awakened={awakened.includes(guardian.id) || undefined}
            style={{ '--guardian-color': guardian.color } as CSSProperties}
            disabled={!ring || isSpinning} aria-label={`Show ${guardian.title} path${ring ? `, ${ring.matches} of five positions matched` : ''}`}
            aria-pressed={selectedGuardian === guardian.id && !isSpinning}
            onPointerEnter={event => { if (event.pointerType === 'mouse') setSelectedGuardian(guardian.id) }}
            onPointerLeave={event => { if (!event.currentTarget.matches(':focus')) setSelectedGuardian(null) }}
            onFocus={() => setSelectedGuardian(guardian.id)} onBlur={() => setSelectedGuardian(null)}
            onClick={() => setSelectedGuardian(guardian.id)}>
            <img className="sunwheel-guardian__portrait" src={artwork[guardian.symbol]} alt="" />
            <div className="sunwheel-guardian__identity"><strong>{guardian.title}</strong>
              <span>{ring ? `${ring.matches} / 5 aligned${awakened.includes(guardian.id) ? ' · awake' : ''}` : 'Match 3 on its path'}</span></div>
            {ring && <GuardianPath ring={ring} rows={feature!.rows} />}
            {ring?.aligned && <span className="sunwheel-guardian__award">{formatRand(slotPointsToRand(ring.awardPoints, pointValueInCents))}</span>}
          </button>
        })}
      </div>
    </div>
    <div className="sunwheel__result" role="status" aria-live="polite" aria-atomic="true">
      <strong>{status}</strong>
      {!isSpinning && feature && feature.totalAwardPoints > 0 && <output className="sunwheel__total" aria-label="Sunwheel award">+{award}</output>}
      <span>{isSpinning ? 'Paths settle with the reels.'
        : hasInvalidFeature ? 'See the settled spin total.'
        : savedRound ? 'Winnings included in the spin total.'
          : pendingMode ? 'Each round charges the saved wager; three wheel turns are included.'
          : feature?.eclipseTriggered ? feature.eclipseMode ? `Three guardians awaken${feature.heart.lit ? ' · heart Sunstone doubles Totality' : ''}.` : 'All three align with the central Sun.'
          : feature?.alignedRings ? `Guardian awards ×${feature.alignmentMultiplier}${feature.eclipseMode
            ? ` · Eclipse ×${feature.wheelAwardScale ?? 1}` : feature.isEclipseSpin ? ' · saved Eclipse spin ×2' : ''}`
            : 'Match three on a guardian path.'}</span>
    </div>
    {feature && tracedGuardian && !isSpinning && <GuardianBoardPath feature={feature} guardian={tracedGuardian} cabinetRef={cabinetRef} />}
    <EclipseCarvings cabinetRef={cabinetRef} mode={mode} awakened={awakened} totality={Boolean(feature?.eclipseTriggered && feature.eclipseMode)} reducedMotion={reducedMotion} />
  </section>
}
