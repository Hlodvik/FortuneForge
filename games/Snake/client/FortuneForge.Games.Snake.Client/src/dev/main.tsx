import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import { SnakeGame } from '../SnakeGame'
import { HttpSnakeGateway } from '../httpSnakeGateway'
import '../snake.css'

const root = document.getElementById('root')
if (!root) throw new Error('Snake preview root was not found.')
createRoot(root).render(<StrictMode><SnakeGame gateway={new HttpSnakeGateway()} /></StrictMode>)
