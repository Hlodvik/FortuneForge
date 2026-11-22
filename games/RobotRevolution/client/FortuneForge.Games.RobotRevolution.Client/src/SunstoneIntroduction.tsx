import { useState } from 'react'
import type { CSSProperties } from 'react'
import { getSlotSymbolDefinition } from '@fortuneforge/slot-client-core'
import type { SlotWinHelpContentProps } from '@fortuneforge/slot-game-runtime/experience'
import { formatCalibratedMultiplier, getSlotEngineGuideContract } from '@fortuneforge/slot-game-runtime/engine-guide'
import { FIFTH_SUN_GAME_ID, FIFTH_SUN_TITLE } from './definition'
import { FIFTH_SUN_PAYING_SYMBOLS, FIFTH_SUN_SYMBOL_SET } from './symbols'
import { getSunwheelPath, SUNWHEEL_GUARDIANS, SUNWHEEL_PAYOUT_TIERS } from './sunwheel'
import type { GuardianId } from './sunwheel'
import { FIFTH_SUN_ECLIPSE_MATH as eclipseMath } from './eclipseMath.generated.js'
import './FifthSunIntroduction.css'

const illustrationRows = 4
const illustrationReels = 5
const symbol = (id: string) => getSlotSymbolDefinition(FIFTH_SUN_SYMBOL_SET, id)

function GuardianIllustration() {
  const [guardianId, setGuardianId] = useState<GuardianId>('jaguar')
  const [dialStep, setDialStep] = useState(0)
  const guardian = SUNWHEEL_GUARDIANS.find(candidate => candidate.id === guardianId)!
  const path = getSunwheelPath(guardianId, illustrationRows, dialStep)
  const points = path.map(cell => `${cell.reel * 100 + 50},${cell.row * 80 + 40}`).join(' ')

  return <section className="fifth-sun-intro__example" aria-labelledby="fifth-sun-example-heading">
    <div className="fifth-sun-intro__example-heading">
      <div><span className="fifth-sun-intro__kicker">Trace the sunwheel</span>
        <h3 id="fifth-sun-example-heading">Three guardians. Three different paths.</h3></div>
      <button type="button" className="fifth-sun-intro__rotate" aria-label="Rotate example calendar"
        onClick={() => setDialStep(step => (step + 1) % illustrationRows)}>
        <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.7" aria-hidden="true">
          <path d="M20 8a8 8 0 1 0 0 8M20 3v5h-5" /></svg>Turn the example
      </button>
    </div>
    <div className="fifth-sun-intro__example-layout" style={{ '--guardian-ink': guardian.color } as CSSProperties}>
      <div className="fifth-sun-intro__choices" role="group" aria-label="Choose an illustrated guardian path">
        {SUNWHEEL_GUARDIANS.map(candidate => <button type="button" key={candidate.id}
          aria-pressed={candidate.id === guardianId} onClick={() => setGuardianId(candidate.id)}>
          <img src={symbol(candidate.symbol).image} alt="" /><span>{candidate.title}</span>
        </button>)}
      </div>
      <figure className="fifth-sun-intro__path-figure">
        <div className="fifth-sun-intro__reel-labels" aria-hidden="true">
          {Array.from({ length: illustrationReels }, (_, index) => <span key={index}>Reel {index + 1}</span>)}
        </div>
        <div className="fifth-sun-intro__path-board" role="img"
          aria-label={`${guardian.title} example path: ${path.map(cell => `reel ${cell.reel + 1}, row ${cell.row + 1}`).join('; ')}`}>
          {Array.from({ length: illustrationRows * illustrationReels }, (_, index) => {
            const reel = index % illustrationReels
            const row = Math.floor(index / illustrationReels)
            const pathIndex = path.findIndex(cell => cell.reel === reel && cell.row === row)
            return <span key={index} className="fifth-sun-intro__path-cell" data-on-path={pathIndex >= 0 || undefined}
              aria-hidden="true">{pathIndex >= 0 ? <b>{pathIndex + 1}</b> : <i>·</i>}</span>
          })}
          <svg viewBox="0 0 500 320" preserveAspectRatio="none" aria-hidden="true"><polyline points={points} /></svg>
        </div>
        <figcaption><strong role="status" aria-live="polite">{guardian.title} · alignment {dialStep + 1} of {illustrationRows}</strong>
          <span>These five marked cells form its path. Any three matching positions can awaken that guardian.</span></figcaption>
      </figure>
    </div>
    <p className="fifth-sun-intro__practice-note">Practice diagram only — turning this example does not place a wager or award a prize.</p>
  </section>
}

export function SunstoneIntroduction({ onClose }: SlotWinHelpContentProps) {
  const { paylinePatternIds, paylinePayouts } = getSlotEngineGuideContract(FIFTH_SUN_GAME_ID)
  const paying = new Set<string>(FIFTH_SUN_PAYING_SYMBOLS)
  const paylineGuide = (FIFTH_SUN_SYMBOL_SET.guideEntries ?? []).filter(entry => paying.has(entry.symbol))
  const eclipseArtworks = [symbol('SOLAR_ECLIPSE'), symbol('LUNAR_ECLIPSE')]

  return <article className="fifth-sun-intro">
    <header className="fifth-sun-intro__hero">
      <div className="fifth-sun-intro__hero-art"><img src={symbol('ECLIPSE').image} alt="The dark sun above the temple" /></div>
      <div className="fifth-sun-intro__hero-copy"><span className="fifth-sun-intro__kicker">The temple of three guardians</span>
        <h2>{FIFTH_SUN_TITLE}</h2><p>Turn the calendar.<br />Awaken the stone guardians.</p>
        <span className="fifth-sun-intro__hero-rule">One board. Three turns towards Totality.</span>
      </div>
    </header>

    <GuardianIllustration />

    <div className="fifth-sun-intro__features">
      <section className="fifth-sun-intro__feature">
        <div className="fifth-sun-intro__feature-art"><img src={symbol('JAGUAR').image} alt="" /><span>01</span></div>
        <h3>Awaken a guardian</h3>
        <p>Match its symbol, a Sunstone or a Wild on any three, four or five cells along its path. Matches do not need to touch.</p>
        <dl className="fifth-sun-intro__tiers">{Object.entries(SUNWHEEL_PAYOUT_TIERS).map(([matches, multiplier]) =>
          <div key={matches}><dt>{matches} matches</dt><dd>{multiplier}×</dd></div>)}</dl>
        <p>These are ordinary Sunwheel awards. Eclipse rounds apply <strong>another ×{eclipseMath.wheelAwardScale}</strong> before the linked guardian multiplier.</p>
      </section>
      <section className="fifth-sun-intro__feature">
        <div className="fifth-sun-intro__feature-art fifth-sun-intro__feature-art--pair">
          <img src={symbol('EAGLE').image} alt="" /><img src={symbol('SERPENT').image} alt="" /><span>02</span></div>
        <h3>Link their power</h3>
        <p>Awaken two guardians for <strong>2× guardian awards</strong>. Awaken all three for <strong>3×</strong>. During Eclipse rounds, their awakenings stay linked across the three alignments.</p>
        <div className="fifth-sun-intro__tribute"><img src={symbol('SUNSTONE').image} alt="Sunstone" />
          <p>Awaken all three during an Eclipse round to reveal <strong>Totality</strong>. A Sunstone at the heart doubles that final award.</p></div>
      </section>
      <section className="fifth-sun-intro__feature">
        <div className="fifth-sun-intro__feature-art fifth-sun-intro__feature-art--pair">
          {eclipseArtworks.map(artwork => <img key={artwork.image} src={artwork.image} alt={artwork.label ?? 'Eclipse Scatter'} />)}<span>03</span></div>
        <h3>One Eclipse awakens the wheel</h3>
        <p>Land <strong>one Solar or Lunar Eclipse on the centre reel</strong> to enter {eclipseMath.eclipseRounds} paid rounds at the triggering wager. Only one Eclipse can be visible.</p>
        <div className="fifth-sun-intro__bonus-badges"><span><b>{eclipseMath.eclipseRounds}</b>paid rounds</span><span><b>{eclipseMath.alignmentsPerRound}</b>alignments per board</span></div>
        <p>Solar turns clockwise through a burning temple. Lunar turns anticlockwise beneath cold turquoise light.</p>
      </section>
    </div>

    <details className="fifth-sun-intro__rules">
      <summary>Symbol values & full rules<span aria-hidden="true">+</span></summary>
      <div className="fifth-sun-intro__rules-body">
        <p>Ordinary wins pay across {paylinePatternIds.length} line patterns. Three or five matching ordinary symbols connect from either edge; a four-symbol ordinary run pays the three-symbol amount. Wilds substitute on three- and five-symbol paylines. Four Wilds do not pay.</p>
        <div className="fifth-sun-intro__table-wrap"><table><caption>Line awards · multiples of the wager · ordinary wins pay once per board</caption>
          <thead><tr><th scope="col">Symbol</th><th scope="col">Ordinary · 3</th><th scope="col">Ordinary · 5</th><th scope="col">Eclipse · 3</th><th scope="col">Eclipse · 5</th></tr></thead>
          <tbody>{paylineGuide.map(entry => <tr key={entry.symbol}><th scope="row"><img src={symbol(entry.symbol).image} alt="" /><span>{symbol(entry.symbol).label}</span></th>
            <td>{entry.firstValue}</td><td>{entry.secondValue ?? '—'}</td>
            <td>{formatCalibratedMultiplier(paylinePayouts![entry.symbol].three, eclipseMath.eclipsePayoutMultiplier)}</td>
            <td>{formatCalibratedMultiplier(paylinePayouts![entry.symbol].five, eclipseMath.eclipsePayoutMultiplier)}</td></tr>)}</tbody>
        </table></div>
        <p>Sunstones set the starting calendar alignment. The heart is reel three, row three. Each paid Eclipse round settles one four-row board, pays ordinary wins once, and then checks three different Sunwheel alignments. Solar advances clockwise; Lunar advances anticlockwise.</p>
        <p>An awakened guardian stays linked until that round ends. Its later path still needs three matching positions to earn another award. All three awakenings reveal Totality once per round, doubled when a Sunstone occupies the heart. Guardian lights reset for the next round.</p>
        <p>The {eclipseMath.eclipseRounds} Eclipse rounds use the triggering wager and each charge that wager; the additional wheel turns are included. Eclipse symbols do not extend or retrigger the mode. Each wager point is worth 25 cents. Line awards round to the nearest point, with half points rounded up; guardian awards round down to whole points and are added to line wins.</p>
        <p>Saved older free bonuses retain their original wager, expanded board and awards. They are completed under their original rules.</p>
      </div>
    </details>
    <footer className="fifth-sun-intro__footer"><p>The symbols decide. The guardians reveal.</p>
      <button type="button" onClick={onClose}>Enter the Eclipse<span aria-hidden="true">→</span></button></footer>
  </article>
}
