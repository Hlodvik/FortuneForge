import { useLayoutEffect } from 'react'
import { SlotsPage } from './features/slots/SlotsPage'
import './App.css'

function App() {
  useLayoutEffect(() => {
    if (window.location.pathname !== '/slots') {
      window.history.replaceState(null, '', '/slots')
    }
  }, [])

  return <SlotsPage />
}

export default App
