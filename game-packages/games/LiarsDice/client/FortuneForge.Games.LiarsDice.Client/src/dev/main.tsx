import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import { LiarsDiceGame } from '../LiarsDiceGame'
import { HttpLiarsDiceGateway } from '../httpLiarsDiceGateway'
import '../liarsDice.css'

const root = document.getElementById('root')
if (!root) throw new Error("Liar's Dice preview root was not found.")
createRoot(root).render(<StrictMode><LiarsDiceGame gateway={new HttpLiarsDiceGateway()} /></StrictMode>)
