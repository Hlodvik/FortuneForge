import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import { CasinoWarGame } from '../CasinoWarGame'
import './preview.css'

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <CasinoWarGame />
  </StrictMode>,
)
