import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import { App } from './App'
import './preview.css'

const root = document.getElementById('root')
if (!root) throw new Error('The Craps preview root was not found.')

createRoot(root).render(
  <StrictMode>
    <App />
  </StrictMode>,
)
