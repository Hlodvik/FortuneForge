// Edited: 2026-11-18T18:24:49-06:00 | Computer: Vinnabok
import type { SlotWinHelpContentProps } from '@fortuneforge/slot-game-runtime/experience'
import { ARCANE_ARCHIVES_VISUALS as art } from './visuals'
import cauldron from './assets/art/optimized/arcane-archives-cauldron-v1.webp'
import './arcaneIntroduction.css'

export function ArcaneIntroduction({ onClose }: SlotWinHelpContentProps) {
  return (
    <div className="arcane-intro">
      <article>
        <img src={art.oghamSlashes} alt="" />
        <div>
          <h3>Payline wins</h3>
          <p>
            Match identical Ogham glyphs, VIIs, or pentagrams. Three matches must start on reel 1 in a
            straight row or diagonal. Four consecutive matches at either edge pay the same as three.
            Five matches pay along any active payline.
          </p>
          <p>Check <strong>Symbol Values</strong> for payouts at your current wager.</p>
        </div>
      </article>
      <article>
        <img src={art.wand} alt="" />
        <div>
          <h3>Wand and constellations</h3>
          <p>
            A wand anywhere collects every visible constellation once. Base payouts are 2× your wager
            for Aether, 4× for Lightning, and 10× for Fortune.
          </p>
        </div>
      </article>
      <article>
        <img src={cauldron} alt="" />
        <div>
          <h3>Cauldron and potions</h3>
          <p>
            Landed ingredients go into the cauldron. It bottles a potion at eight ingredient units
            or five units of the same ingredient. The most common ingredient sets the effect. The latest
            landing breaks ties. The other ingredients add strength.
          </p>
          <p>
            Use one bottle at a time from the <strong>Potion shelf</strong>. It boosts payouts or makes
            certain symbols more common for your next three paid spins.
          </p>
        </div>
      </article>
      <article>
        <img src={art.grandGrimoire} alt="" />
        <div>
          <h3>The spell book</h3>
          <p>
            Land a Grand Grimoire, then choose and activate a <strong>Tome</strong> page. Its two
            ingredients yield two units per landing for your next five paid spins. The Grand Grimoire
            stays off the reels until the bonus ends.
          </p>
        </div>
      </article>
      <button type="button" onClick={onClose}>Enter the Archives</button>
    </div>
  )
}
