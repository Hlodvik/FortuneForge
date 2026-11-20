// Edited: 2026-11-18T18:24:49-06:00 | Computer: Vinnabok
import type { SlotWinHelpContentProps } from '@fortuneforge/slot-game-runtime/experience'
import guideDocument from './assets/guide/illustrated-guide.html?raw'
import { GUIDE_ASSETS } from './guideAssets'
import './arcaneIntroduction.css'

const guide = guideDocument.replace(
  /assets\/[a-z0-9-]+\.(?:webp|png|jpg|svg|woff2)/gi,
  path => GUIDE_ASSETS[path] ?? path,
)

export function ArcaneIntroduction({ onClose }: SlotWinHelpContentProps) {
  return (
    <div className="arcane-intro">
      <iframe
        className="arcane-intro__guide"
        title="Arcane Archives illustrated guide"
        srcDoc={guide}
        tabIndex={0}
      />
      <button className="arcane-intro__enter" type="button" onClick={onClose}>Enter the Archives</button>
    </div>
  )
}
