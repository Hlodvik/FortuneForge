import { midnightHelp, midnightSpecialRound } from '../manifest.js'

export function MidnightHelp() {
  return <>
    <p className="win-help__intro">Wins run from the left or right edge across any of the 23 paylines. Ordinary four-symbol runs pay the three-symbol rate.</p>
    <div className="win-help__rules">
      <section className="win-help__rule"><span className="win-help__rule-number">3+</span><div>
        <h3>{midnightSpecialRound.title}</h3>
        <p>{midnightSpecialRound.earnLabel}</p>
      </div></section>
      {midnightHelp.extraSections?.map(section => <section className="win-help__rule" key={section.badge}>
        <span className="win-help__rule-number">{section.badge}</span><div><h3>{section.title}</h3><p>{section.body}</p></div>
      </section>)}
    </div>
    <p className="win-help__fine-print">{midnightHelp.fiveMatchPayoutNote} {midnightHelp.fourMatchPayoutNote}</p>
  </>
}
