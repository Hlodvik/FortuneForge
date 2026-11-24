import { getSlotSymbolDefinition } from '@fortuneforge/slot-client-core'
import { createClassicPaylineGuide, formatCalibratedMultiplier, getSlotEngineGuideContract } from '@fortuneforge/slot-game-runtime/engine-guide'
import type { SlotWinHelpContentProps } from '@fortuneforge/slot-game-runtime/experience'
import { TREASURE_COVE_TITLE } from './identity'
import { PIRATES_FORTUNE_SYMBOLS } from './symbols'
import './TreasureCoveIntroduction.css'

const guide = getSlotEngineGuideContract('pirates-fortune-v1')
const lineGuide = createClassicPaylineGuide(guide.gameId, ['2', '3', '4', '5', '6', '7', 'ACE', 'POWER'])
const symbol = (id: string) => getSlotSymbolDefinition(PIRATES_FORTUNE_SYMBOLS, id)
const pointValue = new Intl.NumberFormat('en-ZA', { style: 'currency', currency: 'ZAR' }).format(guide.pointValueInCents / 100)
const gemPowers = [
  { symbol: 'SEAL_SYNC', name: 'Ruby', power: 'Matching Reel', detail: 'One reel is copied to create the matching-reel setup during each bonus round.' },
  { symbol: 'SEAL_ROWS', name: 'Lapis', power: 'Extra Rows', detail: 'Two extra rows open above the reels during each bonus round.' },
  { symbol: 'SEAL_PAW', name: 'Orange', power: 'Stronger Purse Hauls', detail: 'Doubloon Purse hauls become stronger during the bonus.' },
  { symbol: 'SEAL_RAND', name: 'Emerald', power: 'Prize Multiplier Column', detail: 'A prize multiplier column appears during each bonus round.' },
] as const

function SymbolArt({ id, className = '' }: { id: string; className?: string }) {
  const artwork = symbol(id)
  return <img className={className} src={artwork.image} alt={artwork.label} loading="lazy" />
}

export function TreasureCoveIntroduction({ onClose }: SlotWinHelpContentProps) {
  return <article className="treasure-cove-guide">
    <header className="treasure-cove-guide__hero">
      <div className="treasure-cove-guide__hero-copy">
        <span className="treasure-cove-guide__eyebrow">A captain’s guide to the cove</span>
        <h2>{TREASURE_COVE_TITLE}</h2>
        <p>Follow the maps.<br />Fill the chests. Bring home the haul.</p>
        <div className="treasure-cove-guide__hero-facts"><span><b>{guide.paylinePatternIds.length}</b>paylines</span><span><b>4</b>gem collections</span></div>
      </div>
      <div className="treasure-cove-guide__hero-art" aria-hidden="true">
        <img src={symbol('PAW').image} alt="" />
        <img src={symbol('RAND_5').image} alt="" />
        <img src={symbol('SEAL_RAND').image} alt="" />
      </div>
    </header>

    <section className="treasure-cove-guide__voyage" aria-labelledby="treasure-cove-voyage-title">
      <div className="treasure-cove-guide__section-heading"><span className="treasure-cove-guide__eyebrow">Chart your course</span><h3 id="treasure-cove-voyage-title">Four ways the cove comes alive</h3></div>
      <div className="treasure-cove-guide__features">
        <section className="treasure-cove-guide__feature">
          <div className="treasure-cove-guide__feature-art"><SymbolArt id="7" /><span>01</span></div>
          <h4>Line up the treasure</h4>
          <p>Match ordinary symbols from either edge along a payline. Three or five matching symbols can pay; four ordinary symbols pay the three-symbol amount.</p>
          <div className="treasure-cove-guide__small-art"><SymbolArt id="ACE" /><p>The moonlit pearl is Wild. It substitutes on five-symbol paylines.</p></div>
        </section>
        <section className="treasure-cove-guide__feature">
          <div className="treasure-cove-guide__feature-art"><SymbolArt id="PAW" /><span>02</span></div>
          <h4>Collect a doubloon haul</h4>
          <p>A Doubloon Purse anywhere in the window collects every visible doubloon. Land two or more purses to double the haul.</p>
          <div className="treasure-cove-guide__coin-range"><SymbolArt id="RAND_05" /><span>Coin face values<br /><b>0.5×–5× wager</b></span></div>
        </section>
        <section className="treasure-cove-guide__feature">
          <div className="treasure-cove-guide__feature-art"><SymbolArt id="FREE" /><span>03</span></div>
          <h4>Follow the Treasure Maps</h4>
          <p>Land <strong>{guide.scatterRequiredSymbols} or more Treasure Map bonus symbols anywhere</strong> to receive {guide.scatterAwardedSpins} Island Search bonus rounds.</p>
          <div className="treasure-cove-guide__bonus-tag"><b>{guide.scatterAwardedSpins}</b><span>Island Search<br />bonus rounds</span></div>
        </section>
        <section className="treasure-cove-guide__feature">
          <div className="treasure-cove-guide__feature-art"><SymbolArt id="BANANA" /><span>04</span></div>
          <h4>Set off a powder-keg volley</h4>
          <p>Three powder-keg stacks in a row, column or diagonal pay <strong>{formatCalibratedMultiplier(3, guide.payoutMultiplier)} your wager</strong> for each qualifying pattern.</p>
          <p className="treasure-cove-guide__muted">These patterns can pay alongside line wins and doubloon hauls.</p>
        </section>
      </div>
    </section>

    <section className="treasure-cove-guide__gems" aria-labelledby="treasure-cove-gems-title">
      <div className="treasure-cove-guide__section-heading"><span className="treasure-cove-guide__eyebrow">Four chests. Four powers.</span><h3 id="treasure-cove-gems-title">Every matching gem brings a bonus closer</h3>
        <p>Gems collect from anywhere in the window. Each matching gem builds its chest and the chance of <strong>{guide.collectionAwardedSpins} enhanced bonus rounds</strong>.</p></div>
      <div className="treasure-cove-guide__gem-grid">{gemPowers.map(gem => <section className="treasure-cove-guide__gem" key={gem.symbol}>
        <SymbolArt id={gem.symbol} />
        <span>{gem.name}</span><h4>{gem.power}</h4><p>{gem.detail}</p>
      </section>)}</div>
      <p className="treasure-cove-guide__gem-note">A chest can award before it is full and awards by {guide.collectionTarget} matching gems. Bonus rounds use the locked wager earned by that collection.</p>
    </section>

    <details className="treasure-cove-guide__rules">
      <summary>Symbol values & full rules<span aria-hidden="true">+</span></summary>
      <div className="treasure-cove-guide__rules-body">
        <p>Wins use {guide.paylinePatternIds.length} full payline patterns across five reels. Ordinary three-symbol matches connect from either edge. Four matching ordinary symbols at either edge pay the three-symbol rate. Four Wilds and four-symbol runs using Wild substitution do not pay.</p>
        <div className="treasure-cove-guide__table-wrap"><table>
          <caption>Line awards · multiples of your wager</caption>
          <thead><tr><th scope="col">Symbol</th><th scope="col">3 symbols</th><th scope="col">5 symbols</th></tr></thead>
          <tbody>{lineGuide.map(entry => <tr key={entry.symbol}><th scope="row"><SymbolArt id={entry.symbol} /><span>{symbol(entry.symbol).label}</span></th><td>{entry.firstValue}</td><td>{entry.secondValue}</td></tr>)}</tbody>
        </table></div>
        <p>Pearl Wilds pay naturally on three or five symbols and substitute only on five-symbol paylines. Natural three-symbol Jolly Roger wins also award one special point; natural five-symbol wins award two. Four-symbol wins award no special points.</p>
        <p>Doubloon face values are added together and doubled when two or more purses land. The total is rounded to a whole wager point, multiplied by {guide.payoutMultiplier}, then rounded again. The powder-keg award shown above includes the same game multiplier.</p>
        <p>Each gem chest keeps the wagers attached to its collected gems. When it awards, the bonus wager is locked to their average, rounded to whole wager points. Multiple earned gem powers can be active together. Saved bonus rounds keep their remaining rounds, original wager and feature state.</p>
        <p>Each wager point is worth {pointValue}. Each paying line match rounds to the nearest point, with half points rounded up. The settled result includes line wins, qualifying powder-keg patterns and doubloon hauls.</p>
      </div>
    </details>

    <footer className="treasure-cove-guide__footer"><p>The map is yours. The next discovery awaits.</p><button type="button" onClick={onClose}>Back to the cove<span aria-hidden="true">→</span></button></footer>
  </article>
}
