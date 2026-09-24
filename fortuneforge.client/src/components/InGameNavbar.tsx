import type { ReactNode } from 'react'
import type { AccountSummary } from '../features/account/services/accountsApi'
import { PlayerAccountMenus } from './PlayerHeader'
import './InGameNavbar.css'

export type InGameNavbarProps = Readonly<{
  account?: AccountSummary
  title?: string
  theme?: 'arcade' | 'casino' | 'cards' | 'slots'
  actions?: ReactNode
  className?: string
}>

export type InGameNavExitsProps = Readonly<{
  authenticated?: boolean
  className?: string
  onNavigate?: () => void
}>

/** Canonical game exits, including the public-vs-account home decision. */
export function InGameNavExits({ authenticated = false, className, onNavigate }: InGameNavExitsProps) {
  return <nav className={['in-game-navbar__exits', className].filter(Boolean).join(' ')} aria-label="Game navigation">
    <a className="player-shell-header__brand" href={authenticated ? '/home' : '/'} aria-label="Fortune Forge home" onClick={onNavigate}>
      <span className="player-shell-header__spark" aria-hidden="true">✦</span>
      <strong>Fortune Forge</strong>
    </a>
    <a className="player-shell-header__games" href={authenticated ? '/games' : '/demo'} onClick={onNavigate}>Other Games</a>
  </nav>
}

/** Host-owned game chrome. The home and library exits are intentionally not configurable. */
export function InGameNavbar({ account, title, theme = 'arcade', actions, className }: InGameNavbarProps) {
  const classes = ['player-shell-header', 'in-game-navbar', `in-game-navbar--${theme}`, className]
    .filter(Boolean)
    .join(' ')

  return <header className={classes} data-game-navbar>
    <InGameNavExits authenticated={account !== undefined} />
    {title && <strong className="in-game-navbar__title">{title}</strong>}
    {actions && <div className="in-game-navbar__actions">{actions}</div>}
    {account && <PlayerAccountMenus account={account} />}
  </header>
}
