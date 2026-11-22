import { useEffect, useState } from 'react'
import type { CSSProperties, RefObject } from 'react'
import { createPortal } from 'react-dom'
import { formatRand, slotPointsToRand } from '@fortuneforge/slot-client-core'
import type { SlotSpinFeaturePanelProps } from '@fortuneforge/slot-game-runtime/experience'
import { decodeSunwheelFeature, SUNWHEEL_GUARDIANS } from './sunwheel'
import type { GuardianId, SunwheelFeature, SunwheelRing } from './sunwheel'
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

function CalendarWheel({ feature, spinning, reducedMotion }: {
  feature: SunwheelFeature | null; spinning: boolean; reducedMotion: boolean
}) {
  return <div className="sunwheel-calendar" data-spinning={spinning || undefined}
    data-eclipse={feature?.eclipseTriggered || undefined} data-reduced-motion={reducedMotion || undefined}
    aria-hidden="true">
    <svg viewBox="0 0 200 200" className="sunwheel-calendar__carving">
      <circle cx="100" cy="100" r="96" className="sunwheel-calendar__rim" />
      <circle cx="100" cy="100" r="91" className="sunwheel-calendar__ticks" />
      {SUNWHEEL_GUARDIANS.map((guardian, index) => {
        const ring = feature?.rings[index]
        const rotation = (ring?.rotationSteps ?? index) * 360 / (feature?.rows ?? 4)
        return <g key={guardian.id} className="sunwheel-calendar__ring" data-aligned={ring?.aligned || undefined}
          style={{ '--ring-turn': `${rotation}deg`, '--guardian-color': guardian.color, '--ring-delay': `${index * 120}ms` } as CSSProperties}>
          <circle cx="100" cy="100" r={78 - index * 17} />
          {Array.from({ length: 12 }, (_, tick) => <path key={tick} transform={`rotate(${tick * 30} 100 100)`}
            d={`M 96 ${25 + index * 17} h 8 l -4 7 Z`} />)}
        </g>
      })}
      <circle cx="100" cy="100" r="24" className="sunwheel-calendar__heart" />
    </svg>
    <img className="sunwheel-calendar__sun" src={artwork.POWER} alt="" />
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

export function SunwheelPanel({ feature: rawFeature, spinKey, isSpinning, reducedMotion, pointValueInCents, cabinetRef }: SlotSpinFeaturePanelProps) {
  const [selectedGuardian, setSelectedGuardian] = useState<GuardianId | null>(null)
  const feature = decodeSunwheelFeature(rawFeature)
  const hasInvalidFeature = rawFeature !== null && rawFeature !== undefined && feature === null
  const savedRound = !hasInvalidFeature && feature === null && spinKey !== null && spinKey > 0 && !isSpinning
  const award = feature ? formatRand(slotPointsToRand(feature.totalAwardPoints, pointValueInCents)) : null
  const status = isSpinning ? 'The calendar turns'
    : hasInvalidFeature ? 'Sunwheel details are unavailable'
      : savedRound ? 'Saved round settled'
        : feature?.eclipseTriggered ? 'Eclipse tribute'
        : feature && feature.alignedRings > 0 ? `${feature.alignedRings} ${feature.alignedRings === 1 ? 'guardian awakens' : 'guardians awaken'}`
          : feature ? 'The guardians rest' : 'Three guardians. One alignment.'

  return <section className="sunwheel" data-eclipse={feature?.eclipseTriggered || undefined}
    data-active-round={feature?.isEclipseSpin || undefined} data-spinning={isSpinning || undefined}
    data-reduced-motion={reducedMotion || undefined} aria-label="Sunwheel Alignment">
    <div className="sunwheel__heading">
      <div><span className="sunwheel__eyebrow">Temple of the Fifth Sun</span><h2>Sunwheel Alignment</h2></div>
      <span className="sunwheel__mode">{savedRound ? 'Saved bonus round' : feature?.isEclipseSpin ? 'Eclipse spins · double wheel awards' : 'A new alignment every spin'}</span>
    </div>
    <div className="sunwheel__body" key={spinKey ?? 'unspun'}>
      <CalendarWheel feature={feature} spinning={isSpinning} reducedMotion={reducedMotion} />
      <div className="sunwheel__guardians" aria-label="Guardian paths">
        {SUNWHEEL_GUARDIANS.map((guardian, index) => {
          const ring = feature?.rings[index]
          return <button type="button" key={guardian.id} className="sunwheel-guardian" data-aligned={ring?.aligned || undefined}
            style={{ '--guardian-color': guardian.color } as CSSProperties}
            disabled={!ring || isSpinning} aria-label={`Show ${guardian.title} path${ring ? `, ${ring.matches} of five positions matched` : ''}`}
            aria-pressed={selectedGuardian === guardian.id && !isSpinning}
            onPointerEnter={event => { if (event.pointerType === 'mouse') setSelectedGuardian(guardian.id) }}
            onPointerLeave={event => { if (!event.currentTarget.matches(':focus')) setSelectedGuardian(null) }}
            onFocus={() => setSelectedGuardian(guardian.id)} onBlur={() => setSelectedGuardian(null)}
            onClick={() => setSelectedGuardian(guardian.id)}>
            <img className="sunwheel-guardian__portrait" src={artwork[guardian.symbol]} alt="" />
            <div className="sunwheel-guardian__identity"><strong>{guardian.title}</strong>
              <span>{ring ? `${ring.matches} / 5 aligned` : 'Match 3 on its path'}</span></div>
            {ring ? <GuardianPath ring={ring} rows={feature!.rows} />
              : <span className="sunwheel-guardian__sleeping" aria-hidden="true">◆ ─ ◆ ─ ◆</span>}
            <span className="sunwheel-guardian__award">{ring?.aligned
              ? formatRand(slotPointsToRand(ring.awardPoints, pointValueInCents)) : '—'}</span>
          </button>
        })}
      </div>
    </div>
    <div className="sunwheel__result" role="status" aria-live="polite" aria-atomic="true">
      <div><strong>{status}</strong><span>{isSpinning ? 'Guardian paths settle with the reels.'
        : savedRound ? 'Your saved round’s winnings are included in the settled total.'
          : feature?.eclipseTriggered ? 'Three guardians and the central Sun. Tribute awarded.'
          : feature?.alignedRings ? `Guardian awards ×${feature.alignmentMultiplier}${feature.isEclipseSpin ? ' · Eclipse spin ×2' : ''}`
            : 'Sunstones and wilds help each guardian path.'}</span></div>
      <output className="sunwheel__total" aria-label="Sunwheel award">{!isSpinning && feature && feature.totalAwardPoints > 0 ? `+${award}` : 'SUNWHEEL'}</output>
    </div>
    <p className="sunwheel__hint">Align Jaguar, Eagle and Serpent, with a Sun at the heart, to reveal the Eclipse tribute.</p>
    {feature && selectedGuardian && !isSpinning && <GuardianBoardPath feature={feature} guardian={selectedGuardian} cabinetRef={cabinetRef} />}
  </section>
}
