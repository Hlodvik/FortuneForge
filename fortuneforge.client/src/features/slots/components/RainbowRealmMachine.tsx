import type { ReactNode } from 'react'
import '../styles/rainbowRealmMachine.css'

type RainbowRealmMachineProps = {
  actionControls?: ReactNode
  className?: string
  controls?: ReactNode
  effects?: ReactNode
  isSpinning?: boolean
  meter?: ReactNode
  reels: ReactNode
  status?: ReactNode
  subtitle?: string
  title?: string
}

const topperLights = Array.from({ length: 9 }, (_, index) => index)
const railLights = Array.from({ length: 6 }, (_, index) => index)

export function RainbowRealmMachine({
  actionControls,
  className,
  controls,
  effects,
  isSpinning = false,
  meter,
  reels,
  status,
  subtitle = 'Fresh picks · bright wins',
  title = 'Rainbow Realm',
}: RainbowRealmMachineProps) {
  const rootClassName = [
    'rainbow-realm-machine',
    isSpinning ? 'rainbow-realm-machine--spinning' : null,
    className,
  ].filter(Boolean).join(' ')

  return (
    <div className={rootClassName} role="group" aria-label={`${title} slot machine`}>
      <div className="rainbow-realm-machine__aura" aria-hidden="true" />
      <div className="rainbow-realm-machine__shell-shine" aria-hidden="true" />

      <header className="rainbow-realm-machine__topper">
        <div className="rainbow-realm-machine__topper-lights" aria-hidden="true">
          {topperLights.map((light) => <i key={light} />)}
        </div>
        <span className="rainbow-realm-machine__eyebrow">Fortune Forge presents</span>
        <strong className="rainbow-realm-machine__title">{title}</strong>
        <span className="rainbow-realm-machine__subtitle">{subtitle}</span>
      </header>

      <div className="rainbow-realm-machine__rail rainbow-realm-machine__rail--left" aria-hidden="true">
        {railLights.map((light) => <i key={light} />)}
      </div>
      <div className="rainbow-realm-machine__rail rainbow-realm-machine__rail--right" aria-hidden="true">
        {railLights.map((light) => <i key={light} />)}
      </div>

      <div className="rainbow-realm-machine__playfield">
        <section className="rainbow-realm-machine__screen" aria-label="Slot game display">
          {meter && <div className="rainbow-realm-machine__meter">{meter}</div>}
          <div className="rainbow-realm-machine__reels">{reels}</div>
          {status && <div className="rainbow-realm-machine__status">{status}</div>}
          <span className="rainbow-realm-machine__screen-glass" aria-hidden="true" />
        </section>

        {actionControls && (
          <aside className="rainbow-realm-machine__actions" aria-label="Spin controls">
            {actionControls}
          </aside>
        )}
      </div>

      <div className="rainbow-realm-machine__deck">
        <div className="rainbow-realm-machine__controls">{controls}</div>
      </div>

      {effects && <div className="rainbow-realm-machine__effects">{effects}</div>}
    </div>
  )
}
