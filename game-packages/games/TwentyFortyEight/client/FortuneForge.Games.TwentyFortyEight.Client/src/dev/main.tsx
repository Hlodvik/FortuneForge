import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import { TwentyFortyEightGame } from '../TwentyFortyEightGame'
import { HttpTwentyFortyEightGateway } from '../httpTwentyFortyEightGateway'
import '../twentyFortyEight.css'

const root = document.getElementById('root')
if (!root) throw new Error('2048 preview root was not found.')
createRoot(root).render(<StrictMode><TwentyFortyEightGame gateway={new HttpTwentyFortyEightGateway()} /></StrictMode>)
