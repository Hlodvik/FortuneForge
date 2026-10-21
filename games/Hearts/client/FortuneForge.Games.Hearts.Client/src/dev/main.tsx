import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import { HeartsGame } from '../HeartsGame'
import { HttpHeartsGateway } from '../httpHeartsGateway'
import { createPreviewGateway } from './previewGateway'
import '../hearts.css'
import './preview.css'

const root = document.getElementById('root')
if (!root) throw new Error('Hearts preview root was not found.')
const mock = import.meta.env.DEV && new URLSearchParams(location.search).get('mock') === '1'
const gateway = mock ? createPreviewGateway() : new HttpHeartsGateway()
createRoot(root).render(<StrictMode><div className="hearts-preview"><header><span className="hearts-preview__logo">✦</span><strong>FORTUNE FORGE</strong><span>HEARTS</span><small>{mock ? 'Preview · setup resets on refresh' : 'Local table'}</small></header><div><HeartsGame gateway={gateway} balance={250} /></div></div></StrictMode>)
