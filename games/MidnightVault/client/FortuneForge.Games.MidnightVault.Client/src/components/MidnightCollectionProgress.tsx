import { ResponsiveSlotImage, getSlotSymbolDefinition, getSlotSymbolImageSrcSet, type SlotSymbolSet } from '@fortuneforge/slot-client-core'
import type { CSSProperties } from 'react'
import type { MidnightCollection, MidnightCollectionId, MidnightSymbolId } from '../types.js'

const pile = [
  { x: 0, y: -.64, rotate: -5, scale: .96 }, { x: -.95, y: 0, rotate: -20, scale: .73 },
  { x: .95, y: -.04, rotate: 18, scale: .74 }, { x: -.53, y: -.45, rotate: -8, scale: .8 },
  { x: .55, y: -.48, rotate: 9, scale: .81 }, { x: -1.34, y: -.61, rotate: -25, scale: .68 },
  { x: 1.31, y: -.67, rotate: 23, scale: .69 }, { x: -.83, y: -1.02, rotate: -14, scale: .76 },
  { x: .84, y: -1.05, rotate: 13, scale: .76 }, { x: 0, y: -1.27, rotate: 2, scale: .83 },
] as const

const labels: Record<MidnightCollectionId, string> = {
  sync: 'Ruby', rows: 'Sapphire', paw: 'Amber', rand: 'Emerald',
}
const symbols: Record<MidnightCollectionId, MidnightSymbolId> = {
  sync: 'SEAL_SYNC', rows: 'SEAL_ROWS', paw: 'SEAL_PAW', rand: 'SEAL_RAND',
}

export function MidnightCollectionProgress({ collection, symbolSet, displayCount, celebrating = false, impacting = false }: {
  collection: MidnightCollection
  symbolSet: SlotSymbolSet<MidnightSymbolId>
  displayCount?: number
  celebrating?: boolean
  impacting?: boolean
}) {
  const count = displayCount ?? collection.count
  const stage = count >= collection.requiredCount || celebrating ? 5 : count <= 0 ? 0
    : Math.min(4, Math.floor(count / collection.requiredCount * 4) + 1)
  const progress = [0, 20, 40, 65, 85, 100][stage]
  const stages = ['Empty pile', 'First jewels', 'Growing pile', 'Rich pile', 'Large pile', 'Vault Breach unlocked']
  const image = getSlotSymbolDefinition(symbolSet, symbols[collection.sealId])
  const hint = `${labels[collection.sealId]} gems: ${stages[stage]}. Matching gems grow this pile toward a 10-spin bonus round. Its wager is the collected gems’ weighted average.`
  return <div
    className={`slots-page__seal-collection slots-page__seal-collection--${collection.sealId} slots-page__seal-collection--vault-access${impacting ? ' slots-page__seal-collection--impact' : ''}${celebrating ? ' slots-page__seal-collection--celebrating' : ''}`}
    data-seal-id={collection.sealId} data-collection-presentation="vault-access" role="img" aria-label={hint}
    style={{ '--collection-progress': `${progress}%` } as CSSProperties}
  >
    <span className="slots-page__collection-visual" aria-hidden="true"><span className="midnight-vault__gem-pile" data-pile-stage={stage}>
      {pile.slice(0, [1, 2, 4, 6, 8, 10][stage]).map((layout, index) => <ResponsiveSlotImage
        className="slots-page__vault-access-gem" key={index} src={image.image}
        imageSrcSet={getSlotSymbolImageSrcSet(image.responsiveImages)} alt="" draggable={false}
        style={{ '--pile-x': `${layout.x * 23}%`, '--pile-y': `${layout.y * 22}%`,
          '--pile-rotate': `${layout.rotate}deg`, '--pile-scale': layout.scale } as CSSProperties}
      />)}
    </span></span>
    <span className="slots-page__seal-details">
      <strong className="slots-page__seal-title">{labels[collection.sealId]}</strong>
      <span className="slots-page__pirate-gem-meter" aria-hidden="true"><span /></span>
    </span>
  </div>
}
