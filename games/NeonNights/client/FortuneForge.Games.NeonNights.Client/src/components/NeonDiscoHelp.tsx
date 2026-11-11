import { discoAwardPercent, discoClusterSizes, discoClusterValues, neonDiscoSymbols } from '../disco.js'

const money = (value: number) => `R${value.toFixed(2)}`

export function NeonDiscoHelp({ wager }: { wager: number }) {
  const wagerCents = Math.round(wager * 100)
  return <>
    <p className="win-help__intro">Match four or more identical symbols touching horizontally or vertically. Groups can take any shape; diagonal touching does not connect them.</p>
    <div className="win-help__rules">
      <article className="win-help__rule">
        <span className="win-help__rule-number">4+</span>
        <div><h3>Connected clusters</h3><p>Each winning group pays more than the wager, even at ×1. The award depends on the symbol and group size. All winning groups pay together and clear, then new symbols drop into the gaps.</p></div>
      </article>
      <article className="win-help__rule">
        <span className="win-help__rule-number">×5</span>
        <div><h3>Build the multiplier</h3><p>Disco starts at ×1. Each winning board raises the multiplier one tier through ×2, ×3 and ×5. You carry that tier into the next spin. A spin with no wins drops it one tier, down to ×1.</p></div>
      </article>
    </div>
    <p className="win-help__note">Awards below use the current {money(wager)} wager at ×1. The active cascade multiplier applies to every winning group.</p>
    <div className="neon-disco-help__payouts" tabIndex={0} role="region" aria-label="Disco cluster payouts">
      <table>
        <caption>Disco cluster payouts</caption>
        <thead><tr><th scope="col">Symbols</th>{discoClusterValues.map(([id, label]) => <th key={id} scope="col">
          <img src={neonDiscoSymbols.definitions[id]!.image} alt={label} />
        </th>)}</tr></thead>
        <tbody>{discoClusterSizes.map(size => <tr key={size}>
          <th scope="row">{size}</th>
          {discoClusterValues.map(([id, , fourSymbolAwardPercent]) => (
            <td key={id}>{money(Math.round(wagerCents * discoAwardPercent(fourSymbolAwardPercent, size) / 100) / 100)}</td>
          ))}
        </tr>)}</tbody>
      </table>
    </div>
    <p className="win-help__fine-print">Disco uses only Jack, Queen, King, Ace, Martini, Neon Seven and Neon Disco Ball. Each spin costs one wager; its following cascades cost nothing extra.</p>
  </>
}
