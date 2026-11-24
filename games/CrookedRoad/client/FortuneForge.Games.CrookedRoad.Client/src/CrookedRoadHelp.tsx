import { CROOKED_ROAD_DEFINITION, CROOKED_ROAD_RULE_CONTRACT as rules } from './definition'
import { CROOKED_ROAD_SYMBOL_IMAGES } from './symbols'
import { ROAD_DESTINATIONS, type RoadDestination } from './road'
import './CrookedRoadHelp.css'

const symbolImages = CROOKED_ROAD_SYMBOL_IMAGES as Readonly<Record<string, string>>

export function CrookedRoadHelp({ onClose }: { onClose: () => void }) {
  return <div className="crooked-road-help">
    <p>Five reels, three rows, and ten winning lines. Build Finbar a road between spins, then spend a funded ticket on an Otherworld journey.</p>
    <section aria-labelledby="crooked-road-help-lines">
      <h3 id="crooked-road-help-lines">The ten lines</h3>
      <p>Three, four or five matching symbols pay from the leftmost reel. Each line awards its highest valid match. Wild hats substitute for paying symbols; an all-wild line uses the fox payout.</p>
      <div className="crooked-road-help__lines">
        {rules.paylines.map((line, index) => <figure key={index}>
          <svg viewBox="0 0 100 60" role="img" aria-label={`Line ${index + 1}: rows ${line.map(row => row + 1).join(', ')}`}>
            {Array.from({ length: 15 }, (_, cell) => <circle key={cell} cx={10 + cell % 5 * 20} cy={10 + Math.floor(cell / 5) * 20} r="2.5" />)}
            <polyline points={line.map((row, reel) => `${10 + reel * 20},${10 + row * 20}`).join(' ')} />
          </svg>
          <figcaption>{index + 1}</figcaption>
        </figure>)}
      </div>
      <table><caption>Line awards as a multiplier of the total wager</caption>
        <thead><tr><th scope="col">Symbol</th><th scope="col">3 / 4 matches</th><th scope="col">5 matches</th></tr></thead>
        <tbody>{CROOKED_ROAD_DEFINITION.symbolGuide?.entries.filter(entry => rules.symbols.some(symbol => symbol.id === entry.symbol && symbol.pays.length > 0)).map(entry =>
          <tr key={entry.symbol}><th scope="row"><img src={symbolImages[entry.symbol]} alt={CROOKED_ROAD_DEFINITION.symbolSpecs[entry.symbol as keyof typeof CROOKED_ROAD_DEFINITION.symbolSpecs][0]} /></th>
            <td>{entry.firstValue}</td><td>{entry.secondValue}</td></tr>)}</tbody>
      </table>
      <p>Awards round down to whole game points. The wager display shows the amount charged for an ordinary paid spin.</p>
    </section>
    <section aria-labelledby="crooked-road-help-map">
      <h3 id="crooked-road-help-map">Build a road, then set off</h3>
      <ol>
        <li>Paid spins earn road tiles. Choose one of the two ready tiles, turn it, and place it in an empty map square.</li>
        <li>Join matching road edges from Finbar’s southern entrance to the {ROAD_DESTINATIONS.well.label} in the west, {ROAD_DESTINATIONS.fort.label} in the north, or {ROAD_DESTINATIONS.hill.label} in the east.</li>
        <li>Tap a placed tile to select it. Rotate or remove it using the map controls. On a keyboard, arrows move between squares; Enter selects or places; R rotates; Delete removes.</li>
        <li>Every {rules.fundingSpins} paid spins earn a journey ticket funded by their average wager. {rules.scatterRequired} or more fairy doors also award a ticket funded by that spin.</li>
        <li>Choose Set off beneath a connected destination. A ticket pays for the journey; its stored funded stake determines the awards. Journey spins have no additional wager.</li>
      </ol>
      <p>Ready tiles, queued tiles, reserve tiles and tickets stay in your saved game state. Building or rearranging the road does not place a wager.</p>
      <p>When Finbar sets off, his route clears from the map and its pieces return to your ready hand or queue. Rearrange and reuse them for your next journey.</p>
    </section>
    <section aria-labelledby="crooked-road-help-journeys">
      <h3 id="crooked-road-help-journeys">Three different journeys</h3>
      {(Object.keys(ROAD_DESTINATIONS) as RoadDestination[]).map(id => <article key={id}>
        <h4>{ROAD_DESTINATIONS[id].label}</h4>
        <p>{id === 'well' ? 'Choose an upper, middle or lower spring. Three free reveals spread a growing ripple of wild hats across the reels from that spring.' :
          id === 'fort' ? 'A separate three-by-three board holds fairy coins. New coins stay in place and restore three respins. The journey ends after three misses, a full board, or its maximum number of reveals.' :
          'Choose one of three doors in each of three chambers. Use Spin after each choice to reveal the server-set prize, then choose again for the next chamber.'}</p>
      </article>)}
    </section>
    <button className="crooked-road-help__close" type="button" onClick={onClose}>Back to the road</button>
  </div>
}
