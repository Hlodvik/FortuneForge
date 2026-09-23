import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import { BaccaratGame } from '../BaccaratGame'
import './preview.css'

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <BaccaratGame />
  </StrictMode>,
)
