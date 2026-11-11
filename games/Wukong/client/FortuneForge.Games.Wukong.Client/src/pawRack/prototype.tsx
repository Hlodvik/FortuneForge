import { createRoot } from 'react-dom/client'
import { isLoopbackPrototypeLocation } from './localOnly'

const container = document.getElementById('root')
if (container) {
  if (!isLoopbackPrototypeLocation(window.location)) {
    container.textContent = 'This synthetic Wukong preview is available on loopback only.'
  } else {
    void import('./WukongPawPrototypeHost').then(({ default: Host }) => {
      createRoot(container).render(<Host />)
    }).catch(() => {
      container.textContent = 'The local preview adapter is unavailable. No spin was submitted.'
    })
  }
}
