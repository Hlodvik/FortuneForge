import type { ReactNode } from 'react'
import { ThreeCardPokerContent, ThreeCardPokerHowToPlay } from './ThreeCardPokerView'
import './threeCardPoker.css'

const views = { Content: ThreeCardPokerContent, HowToPlay: ThreeCardPokerHowToPlay }

export type ThreeCardPokerBrowserViews = typeof views
export type ThreeCardPokerBrowserProps = {
  renderThreeCardPoker: (views: ThreeCardPokerBrowserViews) => ReactNode
}

export default function ThreeCardPokerBrowser({ renderThreeCardPoker }: ThreeCardPokerBrowserProps) {
  return renderThreeCardPoker(views)
}
