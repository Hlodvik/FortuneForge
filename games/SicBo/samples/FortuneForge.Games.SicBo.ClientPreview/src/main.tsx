import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import { SicBoGame } from '@fortuneforge/games-sic-bo'
import '@fortuneforge/games-sic-bo/styles.css'
import './preview.css'

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <SicBoGame />
  </StrictMode>,
)
