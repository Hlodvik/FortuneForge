import { createContext, useContext, type ComponentType, type ReactNode } from 'react'
import type { PlayingCardModel } from '@fortuneforge/client-presentation'

type TableAudioCue = 'shuffle' | 'deal' | 'move' | 'chip' | 'collect' | 'click'

export interface BlackjackTablePresentationServices {
  Loading: ComponentType
  PlayingCard: ComponentType<{ card: PlayingCardModel; faceDown?: boolean }>
  playCardAudio: (cue: TableAudioCue) => void
}

const Presentation = createContext<BlackjackTablePresentationServices | null>(null)

export function BlackjackTablePresentation({ services, children }: {
  services: BlackjackTablePresentationServices
  children: ReactNode
}) {
  return <Presentation.Provider value={services}>{children}</Presentation.Provider>
}

export function useBlackjackTablePresentation() {
  const services = useContext(Presentation)
  if (!services) throw new Error('Blackjack table presentation services are required.')
  return services
}
