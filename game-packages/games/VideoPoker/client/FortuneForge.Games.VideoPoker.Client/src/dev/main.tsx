import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import { VideoPokerGame } from '../VideoPokerGame'
import './preview.css'

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <VideoPokerGame />
  </StrictMode>,
)
