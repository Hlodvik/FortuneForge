import type { ObstacleKind } from './horseFlightSimulation'

export type HorseBiomeObstacleKind = Extract<ObstacleKind,
  'market-cart' | 'hanging-sign' | 'thorn-bramble' | 'spike-barricade' | 'cactus' | 'storm-cloud'>

const biomeObstacleKinds: readonly HorseBiomeObstacleKind[] = [
  'market-cart', 'hanging-sign', 'thorn-bramble', 'spike-barricade', 'cactus', 'storm-cloud',
]

export function isHorseBiomeObstacleKind(kind: ObstacleKind): kind is HorseBiomeObstacleKind {
  return (biomeObstacleKinds as readonly ObstacleKind[]).includes(kind)
}

export function HorseBiomeObstacleArt({ kind, width, height }: Readonly<{
  kind: HorseBiomeObstacleKind
  width: number
  height: number
}>) {
  return <svg className="ff-horse-flight__biome-obstacle-art" width={width} height={height}
    viewBox="0 0 96 72" preserveAspectRatio="none" shapeRendering="crispEdges" aria-hidden="true">
    {kind === 'market-cart' && <>
      <path d="M4 11h88v8H4z" fill="#43303b" />
      <path d="M9 6h78v9H9z" fill="#bd643f" />
      <path d="M9 6h14v9H9zm28 0h14v9H37zm28 0h13v9H65z" fill="#f0c179" />
      <path d="M12 19h7v28h-7zm65 0h7v28h-7z" fill="#573f39" />
      <path d="M6 39h84v15H6z" fill="#654535" />
      <path d="M9 42h78v8H9z" fill="#a47147" />
      <path d="M13 45h69v3H13z" fill="#c89660" />
      <path d="M12 55h71v6H12z" fill="#392c32" />
      <path d="M28 28h12v12H28zm26 2h15v10H54z" fill="#94794b" />
      <path d="M30 29h7v6h-7zm27 2h9v5h-9z" fill="#d3bd6e" />
      <path d="M15 61h14v4H15zm51 0h15v4H66z" fill="#ad8355" />
      <circle cx="22" cy="62" r="9" fill="#302d34" /><circle cx="74" cy="62" r="9" fill="#302d34" />
      <circle cx="22" cy="62" r="4" fill="#bf925b" /><circle cx="74" cy="62" r="4" fill="#bf925b" />
    </>}
    {kind === 'hanging-sign' && <>
      <path d="M9 0h78v8H9z" fill="#51423c" />
      <path d="M18 7h5v14h-5zm56 0h5v14h-5z" fill="#948175" />
      <path d="M18 18h61v38H18z" fill="#44302f" />
      <path d="M23 22h51v29H23z" fill="#98633e" />
      <path d="M27 25h43v3H27zm0 19h43v3H27z" fill="#ca945a" />
      <path d="M27 30h7v11h-7zm12-2h6v15h-6zm11 0h6v15h-6zm12 2h7v11h-7z" fill="#6b4836" />
      <path d="M28 32h40v6H28z" fill="#b98e5a" />
      <path d="M22 53h54v5H22z" fill="#372c31" />
      <path d="M20 14h5v4h-5zm55 0h5v4h-5z" fill="#edc075" />
    </>}
    {kind === 'thorn-bramble' && <>
      <path d="M0 61 10 53 18 58 29 43 38 54 47 35 56 54 67 39 78 54 91 46 96 61v11H0z" fill="#1c312d" />
      <path d="M0 69 16 50 29 65 47 41 60 61 76 39 96 65" fill="none" stroke="#376143" strokeWidth="8" />
      <path d="M4 56 0 37 12 47l5-22 9 25 10-22 8 23 11-31 10 29 16-18 3 27 12-9" fill="none" stroke="#416841" strokeWidth="6" />
      <path d="M9 43 5 32l12 5m18 3-3-14 12 8m19 4 3-14 10 10m9 10 10-13" fill="none" stroke="#b3b775" strokeWidth="4" />
      <path d="M25 57h7v5h-7zm29-8h6v5h-6zm30 7h7v5h-7z" fill="#7e924d" />
    </>}
    {kind === 'spike-barricade' && <>
      <path d="M3 62 14 25 27 57 38 20 50 55 62 18 73 57 85 29 94 62Z" fill="#4e4543" />
      <path d="M8 60 16 30 24 60 38 24 46 60 62 23 69 60 85 34 91 60Z" fill="#9b9b91" />
      <path d="M5 54h86v9H5z" fill="#694335" />
      <path d="M7 56h81v3H7z" fill="#aa6a44" />
      <path d="M11 43 35 60m0-18 24 17m9-18 22 17" fill="none" stroke="#4b302d" strokeWidth="7" />
      <path d="M8 62h83v8H8z" fill="#3a3030" />
      <path d="M14 63h11v3H14zm43 0h9v3h-9z" fill="#c08b54" />
    </>}
    {kind === 'cactus' && <>
      <path d="M43 15h15v49H43z" fill="#2e6852" />
      <path d="M47 9h7v8h-7z" fill="#3e8060" />
      <path d="M22 35h10v18h-10zm5 16h20v10H27z" fill="#337354" />
      <path d="M66 28h10v20H66zM55 45h19v10H55z" fill="#337354" />
      <path d="M47 14h4v47h-4zm-20 25h3v15h-3zm44-7h3v14h-3z" fill="#7bab66" />
      <path d="M39 64h23v5H39zM15 69h70v3H15z" fill="#ab8154" />
      <path d="M41 26h-5m23 2h5m-20 10h-6m22 11h6" stroke="#c9bb80" strokeWidth="3" />
      <path d="M44 21h-6m21 16h7" stroke="#d9ca91" strokeWidth="2" />
    </>}
    {kind === 'storm-cloud' && <>
      <path d="M13 30h13V18h16V9h24v10h13v13h11v16H13z" fill="#354b64" />
      <path d="M17 34h9V21h17V13h21v10h12v12h10v8H17z" fill="#677b92" />
      <path d="M24 36h13V23h14V17h14v18h14v8H24z" fill="#9aabb5" />
      <path d="M7 43h81v7H7z" fill="#26374e" />
      <path d="M57 45 42 58h10l-6 14 27-23H61l10-4Z" fill="#f6df88" />
      <path d="M54 46 44 57h10l-4 9 18-17H59l7-3Z" fill="#fff5ba" />
      <path d="M24 53h7v8h-7zm53 0h6v7h-6z" fill="#84c4d4" />
    </>}
  </svg>
}
