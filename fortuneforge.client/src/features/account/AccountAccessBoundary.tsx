import { useEffect, useRef, useSyncExternalStore, type ReactNode } from 'react'
import { createPortal } from 'react-dom'
import { AccountReviewPage } from '../../pages/account/AccountReviewPage'
import { clearAccountRestriction, getAccountRestriction, subscribeAccountRestriction, type AccountRestrictionNotice } from './accountRestrictions'

const serverSnapshot = () => null

export function AccountAccessBoundary({ children }: { children: ReactNode }) {
  const restriction = useSyncExternalStore(subscribeAccountRestriction, getAccountRestriction, serverSnapshot)
  if (restriction?.code === 'account_restricted') return <AccountReviewPage />
  return <>{children}{restriction && <RestrictionDialog restriction={restriction} />}</>
}

function RestrictionDialog({ restriction }: { restriction: AccountRestrictionNotice }) {
  const dialog = useRef<HTMLElement | null>(null)
  useEffect(() => {
    const previousFocus = document.activeElement instanceof HTMLElement ? document.activeElement : null
    const shell = document.querySelector<HTMLElement>('.app-shell')
    const alreadyInert = shell?.hasAttribute('inert') ?? false
    shell?.setAttribute('inert', '')
    dialog.current?.querySelector<HTMLElement>('button')?.focus()
    const trapFocus = (event: KeyboardEvent) => {
      if (event.key === 'Escape') { event.preventDefault(); clearAccountRestriction(); return }
      if (event.key !== 'Tab') return
      const choices = [...(dialog.current?.querySelectorAll<HTMLElement>('a[href], button') ?? [])]
      const first = choices[0]; const last = choices.at(-1)
      if (event.shiftKey && document.activeElement === first) { event.preventDefault(); last?.focus() }
      else if (!event.shiftKey && document.activeElement === last) { event.preventDefault(); first?.focus() }
    }
    document.addEventListener('keydown', trapFocus)
    return () => {
      document.removeEventListener('keydown', trapFocus)
      if (!alreadyInert) shell?.removeAttribute('inert')
      if (previousFocus?.isConnected) previousFocus.focus()
    }
  }, [])
  return createPortal(<div className="account-review account-restriction-dialog">
    <section className="account-review__card" role="alertdialog" aria-modal="true"
      aria-labelledby="account-restriction-title" aria-describedby="account-restriction-message" ref={dialog}>
      <p className="account-review__brand">Fortune Forge</p>
      <h1 id="account-restriction-title">{restriction.code === 'game_restricted' ? 'Game under review' : 'Withdrawals under review'}</h1>
      <p id="account-restriction-message">{restriction.message}</p>
      <div className="account-review__actions">
        <button type="button" onClick={clearAccountRestriction}>Okay</button>
        {restriction.code === 'game_restricted' && <a className="account-review__button account-review__secondary" href="/home">Return home</a>}
      </div>
    </section>
  </div>, document.body)
}
