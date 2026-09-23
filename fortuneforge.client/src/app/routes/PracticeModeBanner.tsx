export function PracticeModeBanner({ enabled, path }: Readonly<{ enabled: boolean; path: string }>) {
  return <aside className={`wallet-practice${enabled ? ' is-enabled' : ''}`} aria-label="Wallet safety mode">
    <div><strong>{enabled ? 'Practice credits active' : 'Account credits active'}</strong><span>{enabled ? 'Every deal uses a server-isolated R10,000 QA ledger. Your account balance cannot change.' : 'Deals on this table use your account balance.'}</span></div>
    <a href={enabled ? path : `${path}?mode=practice`}>{enabled ? 'Return to account play' : 'Switch to safe practice'}</a>
  </aside>
}
