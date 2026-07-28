import type { CSSProperties, ReactNode } from 'react'
import type { SlotCabinetTheme } from '../config/cabinetThemes'

type CabinetStyle = CSSProperties & Record<`--cabinet-${string}`, string>

type SlotGameFrameProps = {
  cabinetTheme: SlotCabinetTheme
  children: ReactNode
  title?: string
}

export function SlotGameFrame({ cabinetTheme, children, title }: SlotGameFrameProps) {
  const cabinetStyle: CabinetStyle = {
    '--cabinet-panel': cabinetTheme.palette.panel,
    '--cabinet-trim': cabinetTheme.palette.trim,
    '--cabinet-trim-bright': cabinetTheme.palette.trimBright,
    '--cabinet-accent': cabinetTheme.palette.accent,
    '--cabinet-glow': cabinetTheme.palette.glow,
    '--cabinet-text': cabinetTheme.palette.text,
    '--cabinet-visuals-backdrop': cabinetTheme.visualsBackdropImage
      ? `url("${cabinetTheme.visualsBackdropImage}")`
      : 'linear-gradient(transparent, transparent)',
  }

  return (
    <section
      className="slot-game-frame"
      data-cabinet-theme={cabinetTheme.id}
      data-cabinet-chrome={cabinetTheme.chrome}
      style={cabinetStyle}
      aria-label={title ?? cabinetTheme.accessibleName}
    >
      <div className="slot-game-frame__visuals">{children}</div>
    </section>
  )
}
