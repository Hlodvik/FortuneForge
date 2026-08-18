import { useEffect, useState } from 'react'
import { useAuthenticatedAccount } from '../../features/account/useAuthenticatedAccount'
import {
  CardGameLibraryPage,
  type CardGameAvailability,
} from '../../pages/cards/CardGameLibraryPage'
import { AuthenticatedRouteState } from './AuthenticatedRouteState'

const checkingAvailability: CardGameAvailability = {
  blackjack: 'checking',
  texasHoldem: 'checking',
  solitaire: 'checking',
}

export function AuthenticatedCardLibraryRoute() {
  const { account, error, isLoading, reload } = useAuthenticatedAccount('/cards')
  const [availability, setAvailability] = useState<CardGameAvailability>(checkingAvailability)

  useEffect(() => {
    if (account === null) return
    const controller = new AbortController()
    let active = true
    setAvailability(checkingAvailability)
    void Promise.all([
      import('../../games/cards/blackjack/blackjackTableApi')
        .then(({ getBlackjackTableStatus }) => getBlackjackTableStatus(controller.signal))
        .then((status) => status.available)
        .catch(() => false),
      import('../../games/cards/texasHoldem/creditHoldemApi')
        .then(({ getCreditHoldemStatus }) => getCreditHoldemStatus(controller.signal))
        .then((status) => status.available)
        .catch(() => false),
      import('../../games/cards/solitaire/solitaireApi')
        .then(({ getSolitaireSession }) => getSolitaireSession(controller.signal))
        .then(() => true)
        .catch(() => false),
    ]).then(([blackjack, texasHoldem, solitaire]) => {
      if (active) {
        setAvailability({
          blackjack: blackjack ? 'available' : 'unavailable',
          texasHoldem: texasHoldem ? 'available' : 'unavailable',
          solitaire: solitaire ? 'available' : 'unavailable',
        })
      }
    })
    return () => {
      active = false
      controller.abort()
    }
  }, [account])

  if (isLoading || account === null) {
    return <AuthenticatedRouteState error={error} loadingLabel="Opening the card room…" errorTitle="The card room could not be opened." onRetry={reload} />
  }
  return <CardGameLibraryPage account={account} availability={availability} />
}
