import { InGameShell } from '../../../../../../fortuneforge.client/src/components/InGameShell'
import '../../../../../../fortuneforge.client/src/dev/standaloneGamePreview.css'
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
createRoot(root).render(<StrictMode><InGameShell title="Hearts" bodyClassName="standalone-game-preview__body"><HeartsGame gateway={gateway} balance={250} /></InGameShell></StrictMode>)
