import strawberryMascot from '../../assets/mascots/strawberry-mascot.png'
import orchardBase from '../../assets/slots/backgrounds/rainbow-realm-prismatic-orchard-v3-base.png'
import pinwheelFlower from '../../assets/slots/backgrounds/rainbow-realm-pinwheel-flower.png'
import energyBolt from '../../assets/slots/symbols/celestial-lightning-bolt.png'
import cherry from '../../assets/slots/symbols/cherry.gif'
import goldenApple from '../../assets/slots/symbols/golden-apple.gif'
import grape from '../../assets/slots/symbols/grape-bunch.gif'
import lemon from '../../assets/slots/symbols/lemon.gif'
import mango from '../../assets/slots/symbols/mango.gif'
import orange from '../../assets/slots/symbols/orange.gif'
import powerCoin from '../../assets/slots/symbols/rainbow-realm-power-coin.png'
import watermelon from '../../assets/slots/symbols/watermelon-slice.gif'
import { RainbowRealmMachine } from '../../features/slots/components/RainbowRealmMachine'
import '../index.css'

const previewReels = [
  [cherry, grape, lemon],
  [watermelon, orange, cherry],
  [powerCoin, mango, grape],
  [cherry, lemon, watermelon],
  [goldenApple, powerCoin, cherry],
]

const rainbowRealmHud = {
  energy: 64,
  energyCapacity: 100,
  powerSeals: 3,
  powerSealCapacity: 10,
} as const

export function RainbowRealmMachinePreview() {
  return (
    <div className="rainbow-realm-preview">
      <header className="landing-bar rainbow-realm-preview__navbar">
        <a className="landing-brand" href="/" aria-label="Fortune Forge home">
          <span className="landing-brand__spark" aria-hidden="true">✦</span>
          <span>Fortune Forge</span>
        </a>
        <nav className="landing-nav" aria-label="Game navigation">
          <a className="landing-nav__link" href="/slots">All games</a>
          <a className="landing-nav__link" href="/home">Home</a>
        </nav>
      </header>

      <main className="rainbow-realm-preview__scene">
        <img className="rainbow-realm-preview__backdrop" src={orchardBase} alt="" />
        <img
          className="rainbow-realm-preview__flower rainbow-realm-preview__flower--left"
          src={pinwheelFlower}
          alt=""
          aria-hidden="true"
        />
        <img
          className="rainbow-realm-preview__flower rainbow-realm-preview__flower--right"
          src={pinwheelFlower}
          alt=""
          aria-hidden="true"
        />

        <div className="rainbow-realm-preview__machine-wrap">
          <RainbowRealmMachine
            reels={(
              <div className="rainbow-realm-preview__reel-bank" aria-label="Five reels with three fruit symbols each">
                {previewReels.map((symbols, reelIndex) => (
                  <div className="rainbow-realm-preview__reel" key={`preview-reel-${reelIndex}`}>
                    {symbols.map((symbol, rowIndex) => (
                      <span key={`${symbol}-${rowIndex}`}>
                        <img src={symbol} alt="" />
                      </span>
                    ))}
                  </div>
                ))}
              </div>
            )}
            status={(
              <div className="rainbow-realm-preview__status">
                <span>Collect color</span>
                <strong>Ready to spin</strong>
                <span>Chase the rainbow</span>
              </div>
            )}
            controls={(
              <div className="rainbow-realm-preview__controls">
                <div className="rainbow-realm-preview__controls-primary">
                  <div
                    className="rainbow-realm-preview__energy-meter"
                    role="progressbar"
                    aria-label={`Rainbow Realm energy: ${rainbowRealmHud.energy} out of ${rainbowRealmHud.energyCapacity}`}
                    aria-valuemin={0}
                    aria-valuemax={rainbowRealmHud.energyCapacity}
                    aria-valuenow={rainbowRealmHud.energy}
                  >
                    <img src={energyBolt} alt="" aria-hidden="true" />
                    <span>Energy</span>
                    <div className="rainbow-realm-preview__energy-track" aria-hidden="true">
                      <i style={{ right: `${100 - rainbowRealmHud.energy / rainbowRealmHud.energyCapacity * 100}%` }} />
                    </div>
                    <strong>{rainbowRealmHud.energy} / {rainbowRealmHud.energyCapacity}</strong>
                  </div>
                  <div><span>Balance</span><strong>1,250</strong></div>
                  <div><span>Win</span><strong>0</strong></div>
                  <div className="rainbow-realm-preview__wager">
                    <button type="button" aria-label="Decrease bet">−</button>
                    <div><span>Bet</span><strong>50</strong></div>
                    <button type="button" aria-label="Increase bet">+</button>
                  </div>
                </div>
                <div className="rainbow-realm-preview__controls-bonuses">
                  <div
                    className="rainbow-realm-preview__power-meter"
                    role="progressbar"
                    aria-label={`Rainbow Realm fruit power: ${rainbowRealmHud.powerSeals} coins out of ${rainbowRealmHud.powerSealCapacity}`}
                    aria-valuemin={0}
                    aria-valuemax={rainbowRealmHud.powerSealCapacity}
                    aria-valuenow={rainbowRealmHud.powerSeals}
                  >
                    <img src={powerCoin} alt="" aria-hidden="true" />
                    <span>Fruit power</span>
                    <strong>{rainbowRealmHud.powerSeals} / {rainbowRealmHud.powerSealCapacity}</strong>
                    <small>Build power</small>
                  </div>
                </div>
              </div>
            )}
            actionControls={(
              <div className="rainbow-realm-preview__actions">
                <button className="rainbow-realm-preview__spin" type="button">
                  <span aria-hidden="true">▶</span>
                  <strong>Spin</strong>
                </button>
                <button className="rainbow-realm-preview__auto" type="button">
                  <span aria-hidden="true">↻</span>
                  Auto
                </button>
              </div>
            )}
            effects={<span className="rainbow-realm-preview__effect-sparkle" aria-hidden="true" />}
          />
        </div>
      </main>

      <div className="rainbow-realm-preview__mascot" aria-hidden="true">
        <img src={strawberryMascot} alt="" />
      </div>
    </div>
  )
}
