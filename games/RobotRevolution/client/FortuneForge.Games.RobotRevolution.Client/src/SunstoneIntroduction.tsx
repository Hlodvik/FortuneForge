import type { SlotWinHelpContentProps } from '@fortuneforge/slot-game-runtime/experience'
import { createClassicPaylineGuide, getSlotEngineGuideContract, formatCalibratedMultiplier } from '@fortuneforge/slot-game-runtime/engine-guide'
import { ROBOT_REVOLUTION_SYMBOL_IMAGES as artwork } from './symbols'
import { SUNWHEEL_GUARDIANS } from './sunwheel'

export function SunstoneIntroduction({ onClose }: SlotWinHelpContentProps) {
  const paylineGuide = createClassicPaylineGuide('robot-revolution-v1', ['2', '3', '4', '5', '6', '7', 'ACE', 'POWER'])
  const { payoutMultiplier, paylinePatternIds } = getSlotEngineGuideContract('robot-revolution-v1')
  return <article className="sunstone-intro">
    <header className="sunstone-intro__hero">
      <img src={artwork.POWER} alt="" />
      <span>Temple of the Fifth Sun</span>
      <h2>Sunstone <em>Eclipse</em></h2>
      <p>Turn the calendar. Awaken the guardians. Reveal the hidden sun.</p>
    </header>
    <div className="sunstone-intro__steps">
      <section><span className="sunstone-intro__number">I</span><h3>The sun turns the wheel</h3>
        <p>Every settled board sets a new calendar alignment. Golden Sunstones determine how three guardian paths rotate across the reels.</p>
      </section>
      <section><span className="sunstone-intro__number">II</span><h3>Three guardians awaken</h3>
        <div className="sunstone-intro__guardians">{SUNWHEEL_GUARDIANS.map(guardian => <figure key={guardian.id}>
          <img src={artwork[guardian.symbol]} alt="" /><figcaption>{guardian.title}</figcaption>
        </figure>)}</div>
        <p>On each guardian’s path, match its symbol, a Golden Sunstone or a Wild in any three, four or five positions. They do not need to touch.</p>
        <dl className="sunstone-intro__payouts"><div><dt>3 matches</dt><dd>0.5×</dd></div><div><dt>4 matches</dt><dd>1×</dd></div><div><dt>5 matches</dt><dd>2×</dd></div></dl>
        <p>Two aligned guardians multiply all guardian awards by two. Align all three for three times their awards. Each guardian award rounds down to whole wager points, worth 25 cents each.</p>
      </section>
      <section className="sunstone-intro__eclipse"><span className="sunstone-intro__number">III</span><h3>Reveal the Eclipse</h3>
        <p>Align all three guardians while a Golden Sunstone occupies the heart of reel three: row three on the base board, or row four in Eclipse spins. A further <strong>2× your wager</strong> is added as the Eclipse tribute.</p>
        <p>Land three Eclipse scatters anywhere to receive <strong>seven free Eclipse spins</strong>. The reels expand by two rows and all Sunwheel awards, including the tribute, double.</p>
      </section>
      <section><span className="sunstone-intro__number">IV</span><h3>Temple treasures</h3>
        <p>The base game pays across {paylinePatternIds.length} line patterns. Three or five matching ordinary symbols pay from either edge; ordinary four-symbol runs pay the three-symbol amount. Wilds substitute on full five-symbol lines. Four Wilds do not pay.</p>
        <div className="sunstone-intro__symbols">{paylineGuide.map(entry => <figure key={entry.symbol}>
          <img src={artwork[entry.symbol as keyof typeof artwork]} alt={
            ({ '2': 'Cacao', '3': 'Quetzal feather', '4': 'Turquoise earspool', '5': 'Serpent', '6': 'Eagle', '7': 'Jaguar', ACE: 'Wild', POWER: 'Sunstone' } as Record<string, string>)[entry.symbol]} />
          <figcaption><span>3{entry.symbol !== 'ACE' ? '–4' : ''}: {entry.firstValue}</span><span>5: {entry.secondValue}</span></figcaption>
        </figure>)}</div>
        <p>Three Triple Sunstone symbols in a row, column or diagonal pay {formatCalibratedMultiplier(3, payoutMultiplier)} the wager per qualifying pattern. Ordinary wins round to the nearest wager point; half points round up.</p>
      </section>
    </div>
    <footer><p>Sunwheel awards are added to ordinary payline wins. All awards use the current spin’s wager. Free spins use the wager saved when the bonus began.</p>
      <p>Maximum Sunwheel feature award: 20× the wager in a base spin, or 40× during an Eclipse spin. Ordinary wins are separate.</p>
      <button type="button" className="sunstone-intro__enter" onClick={onClose}>Enter the Temple</button>
    </footer>
  </article>
}
