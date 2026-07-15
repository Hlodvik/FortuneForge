import type { ReactNode } from 'react'
import slotMachineWindow from '../../../assets/slots/slot-machine-window.png'

type SlotGameFrameProps = {
  children: ReactNode
  title?: string
}

export function SlotGameFrame({ children, title = 'Fortune Forge Slots' }: SlotGameFrameProps) {
  return (
    <section className="slot-game-frame" aria-label={title}>
      <img className="slot-game-frame__art" src={slotMachineWindow} alt="" aria-hidden="true" />
      <header className="slot-game-frame__header">
        <h1>{title}</h1>
      </header>
      <div className="slot-game-frame__visuals">{children}</div>
    </section>
  )
}
