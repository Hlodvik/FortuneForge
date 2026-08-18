export function GameTypeMenu({
  active,
  demoMode = false,
}: {
  active: 'all' | 'cards' | 'slots' | 'other'
  demoMode?: boolean
}) {
  const slotsHref = demoMode ? '/demo' : '/slots'
  const cardsHref = demoMode ? '/demo/cards' : '/cards'

  return (
    <nav className="game-hub-sidebar" aria-label="Game categories">
      {!demoMode && <a className={active === 'all' ? 'is-active' : ''} href="/games"
        aria-current={active === 'all' ? 'page' : undefined}>All games</a>}
      <a
        className={active === 'slots' ? 'is-active' : ''}
        href={slotsHref}
        aria-current={active === 'slots' ? 'page' : undefined}
      >
        Slot machines
      </a>
      <a
        className={active === 'cards' ? 'is-active' : ''}
        href={cardsHref}
        aria-current={active === 'cards' ? 'page' : undefined}
      >
        Card room
      </a>
      {!demoMode && <a className={active === 'other' ? 'is-active' : ''} href="/games"
        aria-current={active === 'other' ? 'page' : undefined}>Other games</a>}
    </nav>
  )
}
