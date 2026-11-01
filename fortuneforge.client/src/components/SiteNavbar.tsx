import type { ReactNode } from 'react'
import type { AccountSummary } from '../features/account/services/accountsApi'
import { GameReportControl } from './GameReportControl'
import { HeaderBalance } from './HeaderBalance'
import { PlayerAccountMenus } from './PlayerAccountMenus'
import './SiteNavbar.css'

export type SiteNavbarMode = 'home' | 'account' | 'game' | 'public' | 'restricted'

export type SiteNavbarProps = Readonly<{
  mode: SiteNavbarMode
  account?: AccountSummary | null
  authenticated?: boolean
  title?: string
  reportTitle?: string
  theme?: 'arcade' | 'casino' | 'cards' | 'slots'
  actions?: ReactNode
  className?: string
  onNavigate?: () => void
  currentPublicPage?: 'landing' | 'login' | 'create-account' | 'verify-email'
  hidePublicLogin?: boolean
  balance?: number
}>

export type HomeSidebarProps = Readonly<{
  activeNav: 'home' | 'history' | 'account' | null
  onGamesClick?: () => void
  onHelp: () => void
}>

const spadePath = 'M24 2C20 11 5 17 5 27c0 9 10 13 17 5-1 7-5 11-10 13h24c-5-2-9-6-10-13 7 8 17 4 17-5C43 17 28 11 24 2Z'

/** The same mark and wordmark on the Home sidebar and every horizontal navbar. */
export function SiteBrand({ href, stacked = false, className, onNavigate }: {
  href: string
  stacked?: boolean
  className?: string
  onNavigate?: () => void
}) {
  return <a className={['site-navbar__brand', className].filter(Boolean).join(' ')} href={href} aria-label="Fortune Forge home" onClick={onNavigate}>
    <svg className="site-navbar__spade home-lobby__brand-spade" viewBox="0 0 48 48" fill="currentColor" stroke="none" aria-hidden="true" focusable="false"><path d={spadePath} /></svg>
    <strong>{stacked ? <>FORTUNE<br />FORGE</> : 'FORTUNE FORGE'}</strong>
  </a>
}

/** Home's only sidebar; all other screens use the horizontal SiteNavbar. */
export function HomeSidebar({ activeNav, onGamesClick, onHelp }: HomeSidebarProps) {
  return <aside className="home-lobby__sidebar">
    <SiteBrand href="/home" stacked className="home-lobby__brand" />
    <nav className="home-lobby__navigation" aria-label="Player navigation">
      <HomeNavLink href="/home" icon="home" current={activeNav === 'home'}>Home</HomeNavLink>
      <HomeNavLink href="/home#home-lobby-games-title" icon="games" onClick={onGamesClick}>Games</HomeNavLink>
      <HomeNavLink href="/home/history" icon="history" current={activeNav === 'history'}>History</HomeNavLink>
      <HomeNavLink href="/home/settings" icon="account" current={activeNav === 'account'}>Account</HomeNavLink>
    </nav>
    <button className="home-lobby__help" type="button" aria-label="Help" onClick={onHelp}><HomeNavIcon name="help" /> <span>Help</span></button>
  </aside>
}

function HomeNavLink({ href, icon, current, children, onClick }: { href: string; icon: string; current?: boolean; children: ReactNode; onClick?: () => void }) {
  return <a href={href} onClick={onClick} aria-label={typeof children === 'string' ? children : undefined} aria-current={current ? 'page' : undefined}><HomeNavIcon name={icon} /><span>{children}</span></a>
}

function HomeNavIcon({ name }: { name: string }) {
  return <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.6" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">{name === 'home' ? <><path d="m3 10 9-7 9 7v10H5V10" /><path d="M9 20v-7h6v7" /></> : name === 'games' ? <><path d="M7 7h10c3 0 5 11 2 12l-4-4H9l-4 4C2 18 4 7 7 7Z" /><path d="M7 9v5m-2.5-2.5h5m6.5-1h.01m2 3h.01" /></> : name === 'history' ? <><circle cx="12" cy="12" r="9" /><path d="M12 7v5l4 2" /></> : name === 'account' ? <><circle cx="12" cy="7" r="4" /><path d="M5 21v-3c0-6 14-6 14 0v3Z" /></> : <><circle cx="12" cy="12" r="9" /><path d="M9.8 8a2.4 2.4 0 0 1 4.5 1.2c0 2-2.3 2-2.3 4M12 17h.01" /></>}</svg>
}

/** The only site-level navbar. Pages can supply local controls through actions. */
export function SiteNavbar({
  mode, account, authenticated = false, title, reportTitle, theme = 'arcade', actions,
  className, onNavigate, currentPublicPage, balance, hidePublicLogin = false,
}: SiteNavbarProps) {
  if (mode === 'home') {
    return <header className={['site-navbar', 'site-navbar--home', 'home-lobby__topbar', className].filter(Boolean).join(' ')} data-site-navbar>
      {account && <HeaderBalance amount={balance ?? account.balances.slotsCredits} href="/home/rand" />}
      {account && <AccountAvatar account={account} />}
    </header>
  }

  if (mode === 'restricted') {
    return <header className={['site-navbar', 'site-navbar--restricted', className].filter(Boolean).join(' ')} data-site-navbar>
      <span className="site-navbar__brand site-navbar__brand--static"><svg className="site-navbar__spade" viewBox="0 0 48 48" fill="currentColor" stroke="none" aria-hidden="true" focusable="false"><path d={spadePath} /></svg><strong>FORTUNE FORGE</strong></span>
      {actions && <div className="site-navbar__actions">{actions}</div>}
    </header>
  }

  const isPublic = mode === 'public'
  const isGame = mode === 'game'
  const signedIn = authenticated || account !== undefined && account !== null
  const classes = [
    'site-navbar', `site-navbar--${mode}`, 'player-shell-header',
    isGame ? 'in-game-navbar' : '', isGame ? `in-game-navbar--${theme}` : '', className,
  ].filter(Boolean).join(' ')

  return <header className={classes} data-site-navbar data-game-navbar={isGame ? true : undefined}>
    <div className="site-navbar__leading">
      <SiteBrand href={signedIn ? '/home' : '/'} className="player-shell-header__brand" onNavigate={onNavigate} />
      {isGame && <a className="player-shell-header__games" href={signedIn ? '/home#home-lobby-games-title' : '/demo'} onClick={onNavigate}>Other Games</a>}
    </div>
    {!isGame && title && <strong className="in-game-navbar__title">{title}</strong>}
    {actions && <div className="site-navbar__actions in-game-navbar__actions">{actions}</div>}
    {isPublic && <nav className="site-navbar__public-links" aria-label="Primary navigation">
      <a href="/#discover-games">Games</a>
      {!hidePublicLogin && currentPublicPage !== 'login' && <a href="/login">Log in</a>}
      {currentPublicPage !== 'create-account' && <a className="site-navbar__public-cta" href="/create-account">Create account</a>}
    </nav>}
    {mode === 'account' && account && <div className="site-navbar__account-tools"><HeaderBalance amount={balance ?? account.balances.slotsCredits} href="/home/rand" /><AccountAvatar account={account} /></div>}
    {isGame && <div className="in-game-navbar__account-tools">
      {account && <GameReportControl key={account.userId} gameTitle={reportTitle || title} />}
      {(account || balance !== undefined) && <HeaderBalance amount={balance ?? account?.balances.slotsCredits ?? 0} href={account ? '/home/rand' : undefined} />}
      <AccountAvatar account={account} />
    </div>}
  </header>
}

/** The avatar is the same account-menu trigger on Home and on horizontal navbars. */
function AccountAvatar({ account }: { account?: AccountSummary | null }) {
  const initial = account?.playerName.trim().slice(0, 1).toLocaleUpperCase() || 'D'
  return <div className="site-navbar__avatar-account" role={account ? undefined : 'img'} aria-label={account ? undefined : 'Demo player'}>
    <span className="site-navbar__avatar-letter" aria-hidden="true">{initial}</span>
    {account && <PlayerAccountMenus account={account} showBalance={false} />}
  </div>
}
