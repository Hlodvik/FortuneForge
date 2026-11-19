// Edited: 2026-11-18T19:04:55-06:00 | Computer: VINNABOK
import type { SlotPotionBottle } from '@fortuneforge/slot-game-runtime/experience'
import shelfArtwork from './assets/art/optimized/arcane-archives-potion-shelf-v1.webp'
import { getPotionArtwork } from './potionArtwork'
import './potionShelfArtwork.css'

export function ArcanePotionShelfArtwork({ bottles }: { bottles: readonly SlotPotionBottle[] }) {
  return (
    <span className="arcane-potion-rack" aria-hidden="true">
      <img className="arcane-potion-rack__art" src={shelfArtwork} alt="" />
      <span className="arcane-potion-rack__contents">
        {bottles.slice(0, 4).map(bottle => {
          const artwork = getPotionArtwork(bottle.ingredientId)
          return artwork && <img key={bottle.id} className="arcane-potion-rack__bottle" src={artwork.bottle} alt="" />
        })}
      </span>
      <span data-arcane-shelf-ledge />
    </span>
  )
}
