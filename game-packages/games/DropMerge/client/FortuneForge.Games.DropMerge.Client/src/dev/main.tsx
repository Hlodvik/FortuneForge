import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import { DropMergeGame } from '../DropMergeGame'
import { HttpDropMergeGateway } from '../httpDropMergeGateway'
import '../dropMerge.css'
import '../dropMergeColumns.css'
import '../dropMergeColumnSurface.css'
import '../dropMergeTimer.css'
import '../dropMergeTimedColumns.css'
import '../dropMergePalette.css'
import '../dropMergeInteraction.css'

const root = document.getElementById('root')
if (!root) throw new Error('Drop Merge preview root was not found.')
createRoot(root).render(<StrictMode><DropMergeGame gateway={new HttpDropMergeGateway()} /></StrictMode>)
