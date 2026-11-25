export function GameMaintenanceState({ title }: { title: string }) {
  return <main className="game-maintenance" role="status" style={{ margin: '4rem auto', maxWidth: 600, padding: '2rem', textAlign: 'center' }}>
    <p>Under Maintenance</p><h1>{title}</h1><p>This game is temporarily unavailable. Please choose another game.</p>
    <a href="/slots">Return to the slot collection</a>
  </main>
}
