/** The approved, single-ink court symbols. No image requests or per-suit copies. */
export function CourtCardSymbol({ rank }: { rank: 11 | 12 | 13 | 'joker' }) {
  const name = rank === 'joker' ? 'joker' : rank === 11 ? 'jack' : rank === 12 ? 'queen' : 'king'
  return (
    <svg
      className="ff-playing-card__court-art"
      data-court-symbol={name}
      viewBox={rank === 11 ? '45 -48 110 290' : '0 0 120 100'}
      aria-hidden="true"
      focusable="false"
    >
      {rank === 'joker' ? (
        <>
          <path fill="currentColor" d="M25 69C24 54 18 43 13 32C30 28 42 37 47 49C44 29 49 17 60 13C71 17 76 29 73 49C78 37 90 28 107 32C102 43 96 54 95 69Q60 79 25 69Z" />
          <g fill="currentColor"><circle cx="13" cy="28" r="6" /><circle cx="60" cy="7" r="6" /><circle cx="107" cy="28" r="6" /></g>
          <path d="M29 82Q60 91 91 82" fill="none" stroke="currentColor" strokeWidth="8" strokeLinecap="round" />
        </>
      ) : rank === 11 ? (
        <>
          <g fill="currentColor">
            <path d="M100-41.421L121.213 5.247L116.971 141.012Q100 151.012 83.029 141.012L78.787 5.247Z" />
            <path d="M86 164H114V210H86Z" />
            <circle cx="100" cy="215.966" r="18" />
          </g>
          <path d="M56 156Q100 169 144 156" fill="none" stroke="currentColor" strokeWidth="13" strokeLinecap="round" />
        </>
      ) : rank === 12 ? (
        <>
          <path fill="currentColor" d="M56 4H64V12H71V20H64V29H56V20H49V12H56Z" />
          <path fill="currentColor" fillRule="evenodd" d="M26 68L20 44C15 21 38 15 53 28Q60 21 67 28C82 15 105 21 100 44L94 68Q60 80 26 68ZM32 57C24 37 31 26 46 34C41 39 38 48 38 59ZM88 57C96 37 89 26 74 34C79 39 82 48 82 59Z" />
          <path d="M29 82Q60 91 91 82" fill="none" stroke="currentColor" strokeWidth="8" strokeLinecap="round" />
        </>
      ) : (
        <>
          <path fill="currentColor" d="M12 29L35 43L60 15L85 43L108 29L99 72H21Z" />
          <rect x="21" y="79" width="78" height="9" rx="2" fill="currentColor" />
        </>
      )}
    </svg>
  )
}
