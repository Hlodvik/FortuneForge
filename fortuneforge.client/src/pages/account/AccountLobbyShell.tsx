import { useRef, type MouseEvent, type ReactNode } from 'react'
import '@fontsource-variable/sora'
import type { AccountSummary } from '../../features/account/services/accountsApi'
import { GameReportControl } from '../../components/GameReportControl'
import { HomeSidebar, SiteNavbar } from '../../components/SiteNavbar'
import './homeLobby.css'

export function AccountLobbyShell({ account, activeNav, onOpenOverlay, homeLayout = false, children }: {
  account: AccountSummary | null
  activeNav: 'home' | 'history' | 'account' | null
  onOpenOverlay?: (overlay: 'history' | 'settings') => void
  homeLayout?: boolean
  children: ReactNode
}) {
  const helpDialog = useRef<HTMLDialogElement>(null)

  function handleOverlayLink(event: MouseEvent<HTMLDivElement>) {
    if (!onOpenOverlay || event.defaultPrevented || event.button !== 0 || event.metaKey || event.ctrlKey || event.shiftKey || event.altKey) return
    if (!(event.target instanceof Element)) return
    const link = event.target.closest('a[href]')
    if (!link || !event.currentTarget.contains(link) || link.hasAttribute('download') || (link.getAttribute('target') && link.getAttribute('target') !== '_self')) return
    const destination = new URL(link.getAttribute('href') ?? '', window.location.href)
    if (destination.origin !== window.location.origin) return
    if (destination.pathname === '/home/history' || destination.pathname === '/home/settings') {
      event.preventDefault()
      helpDialog.current?.close?.()
      onOpenOverlay(destination.pathname === '/home/history' ? 'history' : 'settings')
    }
  }

  return (
    <div className={`home-lobby${homeLayout ? '' : ' home-lobby--compact'}`} onClick={handleOverlayLink}>
      {homeLayout && <HomeSidebar activeNav={activeNav} onHelp={() => helpDialog.current?.showModal()} />}
      {!homeLayout && <SiteNavbar mode="account" account={account} />}
      <main className="home-lobby__main">
        {homeLayout && <SiteNavbar mode="home" account={account} />}
        {children}
      </main>
      {homeLayout && <dialog className="home-lobby__help-dialog" ref={helpDialog} aria-labelledby="home-lobby-help-title"><form method="dialog"><button className="home-lobby__help-close" aria-label="Close help">×</button></form><small>A little help</small><h2 id="home-lobby-help-title">What can we help with?</h2><p>Each game has its own how-to-play guide. For your account and payments, start here.</p><a href="/home/settings">Account settings <span aria-hidden="true">→</span></a><a href="/balance">Funds and withdrawals <span aria-hidden="true">→</span></a><a href="/home/history">Your game history <span aria-hidden="true">→</span></a><GameReportControl gameTitle="the player lobby" /></dialog>}
    </div>
  )
}
