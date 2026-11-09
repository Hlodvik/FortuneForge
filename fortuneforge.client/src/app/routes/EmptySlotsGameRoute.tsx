import { InGameNavbar } from '../../components/InGameNavbar'
import './EmptySlotsGameRoute.css'

export function EmptySlotsGameRoute() {
  return <div className="empty-slots-game">
    <InGameNavbar authenticated title="Empty Slots Game" />
    <main className="empty-slots-game__surface" aria-label="Empty Slots Game" />
  </div>
}
