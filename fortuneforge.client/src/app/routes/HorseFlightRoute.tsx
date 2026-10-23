import { useCallback, useMemo, useState } from 'react'
import {
  HorseFlightGame,
  type HorseFlightEndScreenState,
  type HorseFlightLaunchScreenState,
  type HorseFlightResult,
} from '@fortuneforge/games-horse-flight'
import { InGameShell } from '../../components/InGameShell'
import { fetchWithAccountSession } from '../../features/account/services/accountsApi'
import { useAuthenticatedAccount } from '../../features/account/useAuthenticatedAccount'
import { ArcadeCompetitionLeaderboard } from '../../games/arcade/ArcadeCompetitionLeaderboard'
import { HttpArcadeCompetitionGateway, type ArcadeCompetitionPaidPeriod } from '../../games/arcade/arcadeCompetitionApi'
import { AuthenticatedRouteState } from './AuthenticatedRouteState'
import {
  HorseFlightCompetitionGatewayAdapter,
  type HorseFlightPlayMode,
} from './horseFlightCompetitionGateway'
import '@fortuneforge/games-horse-flight/styles.css'
import '../../pages/playerShell.css'
import './HorseFlightRoute.css'

type HorseFlightSession = Readonly<{
  autoStart: boolean
  id: number
  mode: HorseFlightPlayMode
}>

type HorseFlightLaunchView = 'menu' | 'ranked' | 'leaderboard' | 'instructions'

export function AuthenticatedHorseFlightRoute() {
  const { account, error, isLoading, reload } = useAuthenticatedAccount('/games/horse-flight')
  const competitionGateway = useMemo(() => new HttpArcadeCompetitionGateway(fetchWithAccountSession), [])
  const [session, setSession] = useState<HorseFlightSession>({ autoStart: false, id: 0, mode: { kind: 'free' } })
  const [launchView, setLaunchView] = useState<HorseFlightLaunchView>('menu')
  const [showRankedChoices, setShowRankedChoices] = useState(false)
  const [leaderboardVersion, setLeaderboardVersion] = useState(0)

  const refreshPaidAccount = useCallback(() => { void reload() }, [reload])
  const onResult = useCallback((result: HorseFlightResult) => {
    if (result.runId.startsWith('horse_flight_') && !result.runId.startsWith('horse_flight_free_')) {
      setLeaderboardVersion((version) => version + 1)
    }
  }, [])
  const beginRun = useCallback((mode: HorseFlightPlayMode) => {
    setLaunchView('menu')
    setShowRankedChoices(false)
    setSession((current) => ({ autoStart: true, id: current.id + 1, mode }))
  }, [])
  const playRanked = useCallback((period: ArcadeCompetitionPaidPeriod) => beginRun({ kind: 'paid', period }), [beginRun])

  const horseGateway = useMemo(() => new HorseFlightCompetitionGatewayAdapter(
    competitionGateway,
    session.mode,
    account?.balances.slotsCredits ?? 0,
    refreshPaidAccount,
  ), [account?.balances.slotsCredits, competitionGateway, refreshPaidAccount, session.mode])

  if (isLoading || account === null) {
    return <AuthenticatedRouteState error={error} loadingLabel="Opening Horse Flight…" errorTitle="Horse Flight could not be opened." onRetry={reload} />
  }

  const renderLaunchScreen = ({ available, connecting, error: launchError, retryConnection }: HorseFlightLaunchScreenState) => (
    <div className="horse-flight-launch" data-view={launchView}>
      <div className="horse-flight-launch__heading">
        <h1>HORSE FLIGHT</h1>
        <p>Ready to run?</p>
      </div>

      {launchView === 'menu' && <div aria-label="Horse Flight menu" className="horse-flight-launch__menu">
        <button className="horse-flight-launch__button horse-flight-launch__button--ranked" disabled={!available} onClick={() => setLaunchView('ranked')} type="button">Play Ranked · R1</button>
        <button className="horse-flight-launch__button horse-flight-launch__button--free" disabled={!available} onClick={() => beginRun({ kind: 'free' })} type="button">Play Free</button>
        <button className="horse-flight-launch__button" onClick={() => setLaunchView('leaderboard')} type="button">View Leaderboard</button>
        <button className="horse-flight-launch__button" onClick={() => setLaunchView('instructions')} type="button">Instructions</button>
      </div>}

      {launchView === 'ranked' && <section aria-labelledby="horse-flight-ranked-title" className="horse-flight-launch__panel horse-flight-launch__ranked">
        <h2 id="horse-flight-ranked-title">Choose a ranked flight</h2>
        <div>
          <button disabled={!available} onClick={() => playRanked('daily')} type="button">Daily · R1</button>
          <button disabled={!available} onClick={() => playRanked('weekly')} type="button">Weekly · R1</button>
        </div>
        <button className="horse-flight-launch__back" onClick={() => setLaunchView('menu')} type="button">← Back</button>
      </section>}

      {launchView === 'leaderboard' && <section aria-label="Horse Flight leaderboard view" className="horse-flight-launch__panel horse-flight-launch__leaderboard">
        <ArcadeCompetitionLeaderboard key={leaderboardVersion} gameId="horse-flight" gateway={competitionGateway} pageSize={5} />
        <button className="horse-flight-launch__back" onClick={() => setLaunchView('menu')} type="button">← Back</button>
      </section>}

      {launchView === 'instructions' && <section aria-labelledby="horse-flight-instructions-title" className="horse-flight-launch__panel horse-flight-launch__instructions">
        <h2 id="horse-flight-instructions-title">How to ride</h2>
        <ul>
          <li><strong>Jump</strong><span>Tap, press Space, or use A. Jump again in the air for extra height.</span></li>
          <li><strong>Dash</strong><span>Hold the Dash control, right mouse button, or B to duck high hazards and fast-fall.</span></li>
          <li><strong>Pause</strong><span>Press P, Escape, or Start whenever you need a breather.</span></li>
        </ul>
        <button className="horse-flight-launch__back" onClick={() => setLaunchView('menu')} type="button">← Back</button>
      </section>}

      {launchView === 'menu' && connecting && <p className="horse-flight-launch__status" role="status">Opening the trail…</p>}
      {launchError && <div className="horse-flight-launch__error" role="alert">
        <span>{launchError}</span>
        {!available && <button onClick={retryConnection} type="button">Retry</button>}
      </div>}
    </div>
  )

  const renderEndScreen = ({ error: saveError, isSaving, result, retrySave, score, terminalMessage, terminalObstacleKind }: HorseFlightEndScreenState) => (
    <div className="horse-flight-result">
      <div className="horse-flight-result__outcome" data-terminal-obstacle={terminalObstacleKind ?? 'fall'}>
        <strong>{terminalMessage}</strong>
        <span>Score {score}</span>
      </div>
      <ArcadeCompetitionLeaderboard key={leaderboardVersion} gameId="horse-flight" gateway={competitionGateway} pageSize={5} />
      {saveError && <div className="horse-flight-result__error" role="alert">
        <span>{saveError}</span>
        <button onClick={retrySave} type="button">Retry</button>
      </div>}
      {showRankedChoices
        ? <div aria-label="Choose ranked leaderboard" className="horse-flight-result__ranked">
          <button disabled={isSaving || result === null} onClick={() => void playRanked('daily')} type="button">Daily · R1</button>
          <button disabled={isSaving || result === null} onClick={() => void playRanked('weekly')} type="button">Weekly · R1</button>
          <button className="horse-flight-result__back" onClick={() => setShowRankedChoices(false)} type="button">Back</button>
        </div>
        : <div className="horse-flight-result__actions">
          <button disabled={isSaving || result === null} onClick={() => beginRun({ kind: 'free' })} type="button">Play Free</button>
          <button disabled={isSaving || result === null} onClick={() => setShowRankedChoices(true)} type="button">Play Ranked</button>
        </div>}
    </div>
  )

  return <InGameShell account={account} theme="arcade" className="player-page horse-flight-page">
    <HorseFlightGame
      autoStart={session.autoStart}
      debugFreePlay={import.meta.env.DEV && session.mode.kind === 'free'}
      gateway={horseGateway}
      key={session.id}
      onResult={onResult}
      playerId={account.userId}
      renderEndScreen={renderEndScreen}
      renderLaunchScreen={renderLaunchScreen}
    />
  </InGameShell>
}
