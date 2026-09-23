import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import { SicBoGame } from '@fortuneforge/games-sic-bo'
import './preview.css'

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <SicBoGame />
  </StrictMode>,
)
