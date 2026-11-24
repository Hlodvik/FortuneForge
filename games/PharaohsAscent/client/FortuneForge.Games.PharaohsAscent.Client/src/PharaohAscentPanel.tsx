import { useState, type CSSProperties } from 'react'
import { decodePharaohFeature, decodePharaohState, GUARDIANS, type GuardianId } from './pharaoh'
import { PHARAOHS_ASCENT_VISUALS as art } from './visuals'

const currency = new Intl.NumberFormat('en-ZA', { style: 'currency', currency: 'ZAR' })

export type PharaohAscentPanelProps = Readonly<{
  state: unknown | null
  feature: unknown | null
  spinKey: number | null
  isSpinning: boolean
  reducedMotion: boolean
  pointValueInCents: number
  onOpen?: () => void
}>

export function PharaohAscentPanel({ state, feature, isSpinning, spinKey, reducedMotion, pointValueInCents, onOpen }: PharaohAscentPanelProps) {
  const [selected, setSelected] = useState<GuardianId>('sync')
  const progress = decodePharaohState(state)
  const receipt = !isSpinning && spinKey !== null ? decodePharaohFeature(feature) : null
  const guardian = GUARDIANS.find(entry => entry.id === selected)!
  const collection = progress?.collections.find(entry => entry.sealId === selected)
  const modes = progress?.freeSpinFeatureMode?.split('-') ?? []
  const tombActive = (progress?.freeSpinsRemaining ?? 0) > 0
  const energy = Math.min(100, progress?.energyBalance ?? 0)
  const trail = receipt?.scarabTrails[0]
  const trailRows = Math.max(4, ...(receipt?.scarabTrails.flatMap(group => group.positions.map(position => position.row + 1)) ?? []))

  return <section className="pharaoh-ascent" aria-label="Pyramid guardian chambers" data-tomb-active={tombActive || undefined} data-reduced-motion={reducedMotion || undefined}>
    <header className="pharaoh-ascent__heading">
      <span className="pharaoh-ascent__eyebrow">Beyond the golden gates</span>
      <h2>The guardian chambers</h2>
      <p>Restore a seal. Awaken its guardian.</p>
    </header>
    <div className="pharaoh-ascent__monument" aria-label="Your four guardian seal collections">
      <img className="pharaoh-ascent__capstone" src={art.gate} alt="" />
      <div className="pharaoh-ascent__pyramid" aria-hidden="true" />
      {GUARDIANS.map((entry, index) => {
        const track = progress?.collections.find(value => value.sealId === entry.id)
        const active = tombActive && modes.includes(entry.id)
        const count = track ? Math.min(track.requiredCount, track.count) : null
        return <button key={entry.id} type="button" className="pharaoh-ascent__chamber"
          style={{ '--guardian-color': entry.color, '--tier-width': `${48 + index * 16}%` } as CSSProperties}
          aria-pressed={selected === entry.id} data-awake={active || undefined}
          aria-label={`${entry.name}: ${count === null ? 'progress loading' : `${count} of ${track!.requiredCount} seals`}${active ? ', guardian active' : ''}`}
          onClick={() => { setSelected(entry.id); onOpen?.() }}>
          <img src={entry.image} alt="" /><span className="pharaoh-ascent__guardian-name">{entry.name}</span>
          <span className="pharaoh-ascent__seal-count">{count === null ? '—' : count}<small> / {track?.requiredCount ?? 24}</small></span>
          <span className="pharaoh-ascent__seal-track" aria-hidden="true"><i style={{ width: `${track ? Math.min(100, track.count / track.requiredCount * 100) : 0}%` }} /></span>
        </button>
      })}
    </div>
    <div className="pharaoh-ascent__guardian-detail" style={{ '--guardian-color': guardian.color } as CSSProperties}>
      <span className="pharaoh-ascent__eyebrow">{guardian.name} · {guardian.title}</span>
      <p>{guardian.effect}</p>
      <span>{collection ? `${Math.max(0, collection.requiredCount - collection.count)} seals to 7 enhanced Tomb Spins` : 'Loading your saved seals…'}</span>
    </div>
    <div className="pharaoh-ascent__sunfire">
      <img src={art.sunfire} alt="" />
      <div><span>Sunfire <strong>{progress ? `${energy}/100` : '—'}</strong></span>
        <div role="progressbar" aria-label="Sunfire" aria-valuemin={0} aria-valuemax={100} aria-valuenow={progress ? energy : undefined} className="pharaoh-ascent__sunfire-track"><i style={{ width: `${energy}%` }} /></div>
      </div>
      <small>Full Sunfire: at a steady bet, the next paying spin pays 1.5× and completes the nearest seal. Changing your bet keeps the bonus tied to the wagers that filled the meter; fractional bonuses round down.</small>
    </div>
    <div className="pharaoh-ascent__tomb-status" role="status" aria-live="polite">
      {tombActive ? <><img src={art.gate} alt="" /><span><strong>{progress!.freeSpinsRemaining} Tomb Spins remaining</strong><small>{modes.length ? `Awakened: ${GUARDIANS.filter(entry => modes.includes(entry.id)).map(entry => entry.name).join(' + ')}` : 'The pyramid gates are open'} · locked wager</small></span></>
        : <><img src={art.gate} alt="" /><span><strong>3 gates unlock 6 Tomb Spins</strong><small>Guardian collections persist between paid spins.</small></span></>}
    </div>
    <details className="pharaoh-ascent__trail" onToggle={event => { if (event.currentTarget.open) onOpen?.() }} open={trail ? true : undefined}>
      <summary><img src={art.trail} alt="" /><span>Scarab Trail</span><strong>{receipt && receipt.scarabTrailPoints > 0 ? `+${currency.format(receipt.scarabTrailPoints * pointValueInCents / 100)}` : '3 · 4 · 5+'}</strong></summary>
      {trail ? <div className="pharaoh-ascent__trail-receipt">
        <div className="pharaoh-ascent__trail-grid" style={{ '--trail-rows': trailRows } as CSSProperties} aria-label="Scarab positions on the settled reels">
          {Array.from({ length: trailRows * 5 }, (_, index) => {
            const found = receipt!.scarabTrails.some(group => group.positions.some(position => position.reel === index % 5 && position.row === Math.floor(index / 5)))
            return <span key={index} data-scarab={found || undefined}>{found && <img src={art.trail} alt={`Scarab, reel ${index % 5 + 1}, row ${Math.floor(index / 5) + 1}`} />}</span>
          })}
        </div>
        <p>{receipt!.scarabTrails.map(group => `${group.symbolCount} connected scarabs`).join(' + ')}. This reward is included in your settled spin total.</p>
      </div> : <p>Connect at least 3 red scarabs edge to edge. Trails may bend; diagonal corners do not connect. Larger groups unlock higher rewards.</p>}
      <div className="pharaoh-ascent__trail-tiers"><span>3 scarabs <b>3×</b></span><span>4 scarabs <b>5×</b></span><span>5+ scarabs <b>8×</b></span></div>
      <small>Trail multipliers apply to the reel wager. Saved funded wagers stay locked.</small>
    </details>
  </section>
}
