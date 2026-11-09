// Payline diagrams live outside the page component so the help UI only renders
// rules and does not define them.
export const THREE_MATCH_PATTERNS = [
  { label: 'Top row straight across', rows: [0, 0, 0] },
  { label: 'Upper middle row straight across', rows: [1, 1, 1] },
  { label: 'Lower middle row straight across', rows: [2, 2, 2] },
  { label: 'Bottom row straight across', rows: [3, 3, 3] },
  { label: 'Diagonal down from the top row', rows: [0, 1, 2] },
  { label: 'Diagonal down from the upper middle row', rows: [1, 2, 3] },
  { label: 'Diagonal up from the bottom row', rows: [3, 2, 1] },
  { label: 'Diagonal up from the lower middle row', rows: [2, 1, 0] },
] as const

export const FIVE_MATCH_PATTERNS = [
  [0, 0, 0, 0, 0], [1, 1, 1, 1, 1], [2, 2, 2, 2, 2], [3, 3, 3, 3, 3],
  [0, 1, 2, 1, 0], [1, 2, 3, 2, 1], [3, 2, 1, 2, 3], [2, 1, 0, 1, 2],
  [0, 1, 1, 1, 0], [1, 2, 2, 2, 1], [2, 3, 3, 3, 2], [3, 2, 2, 2, 3],
  [2, 1, 1, 1, 2], [0, 0, 1, 2, 3], [3, 3, 2, 1, 0], [0, 1, 2, 3, 3],
  [3, 2, 1, 0, 0], [0, 1, 0, 1, 0], [1, 2, 1, 2, 1], [2, 3, 2, 3, 2],
  [1, 0, 1, 0, 1], [2, 1, 2, 1, 2], [3, 2, 3, 2, 3],
] as const
