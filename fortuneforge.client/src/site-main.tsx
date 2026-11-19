// Edited: 2026-11-18T22:49:02-06:00 | Computer: Vinnabok
import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import './index.css'
import { SiteApp } from './app/SiteApp'

createRoot(document.getElementById('root')!).render(<StrictMode><SiteApp /></StrictMode>)
