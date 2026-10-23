type SkeletonHoundArtProps = Readonly<{
  width?: number
  height?: number
  frame?: number
  knockedDown?: boolean
}>

type RunPose = Readonly<{
  bob: number
  farForeleg: string
  nearForeleg: string
  farHindleg: string
  nearHindleg: string
  tail: string
}>

const runPoses: readonly RunPose[] = [
  { bob: 0, farForeleg: 'M20 22 25 27 31 31', nearForeleg: 'M17 22 11 29 4 33', farHindleg: 'M40 23 34 28 28 32', nearHindleg: 'M41 23 47 29 54 32', tail: 'M44 17 50 11 56 9' },
  { bob: 1, farForeleg: 'M20 22 23 29 28 33', nearForeleg: 'M17 22 13 29 8 31', farHindleg: 'M40 23 35 27 32 29', nearHindleg: 'M41 23 49 28 53 29', tail: 'M44 17 51 13 56 12' },
  { bob: -1, farForeleg: 'M20 22 14 27 7 31', nearForeleg: 'M17 22 22 28 29 33', farHindleg: 'M40 23 47 28 55 33', nearHindleg: 'M41 23 36 29 29 32', tail: 'M44 17 50 9 56 8' },
  { bob: 0, farForeleg: 'M20 22 13 29 8 32', nearForeleg: 'M17 22 23 28 31 31', farHindleg: 'M40 23 48 29 54 31', nearHindleg: 'M41 23 35 27 29 31', tail: 'M44 17 51 12 56 10' },
  { bob: 1, farForeleg: 'M20 22 16 27 11 29', nearForeleg: 'M17 22 22 29 27 33', farHindleg: 'M40 23 48 27 52 29', nearHindleg: 'M41 23 37 28 33 31', tail: 'M44 17 52 14 56 14' },
  { bob: -1, farForeleg: 'M20 22 26 28 32 33', nearForeleg: 'M17 22 10 28 3 32', farHindleg: 'M40 23 34 29 28 33', nearHindleg: 'M41 23 48 28 55 31', tail: 'M44 17 50 10 56 7' },
]

/** Transparent pixel-style replacement for the opaque skeleton-dog sprite sheets. */
export function SkeletonHoundArt({ width = 58, height = 38, frame = 0, knockedDown = false }: SkeletonHoundArtProps) {
  const index = ((Math.floor(frame) % runPoses.length) + runPoses.length) % runPoses.length
  return <svg
    className="ff-horse-flight__skeleton-hound-art"
    data-hound-pose={knockedDown ? 'knockdown' : 'run'}
    data-hound-frame={knockedDown ? undefined : index}
    x="0"
    y="0"
    width={width}
    height={height}
    viewBox="0 0 58 38"
    preserveAspectRatio="none"
    overflow="hidden"
    shapeRendering="crispEdges"
    aria-hidden="true"
  >
    <ellipse cx="30" cy="36" rx="24" ry="2" fill="#171411" opacity=".22" />
    {knockedDown ? <KnockedDownHound /> : <RunningHound pose={runPoses[index]} />}
  </svg>
}

function Bone({ d, shade = false }: Readonly<{ d: string; shade?: boolean }>) {
  return <g fill="none" strokeLinecap="square" strokeLinejoin="miter">
    <path d={d} stroke="#33271e" strokeWidth="5" />
    <path d={d} stroke={shade ? '#b89e75' : '#e8d4a5'} strokeWidth="3" />
  </g>
}

function RunningHound({ pose }: Readonly<{ pose: RunPose }>) {
  return <g transform={`translate(0 ${pose.bob})`}>
    <Bone d={pose.tail} shade />
    <Bone d={pose.farHindleg} shade />
    <Bone d={pose.farForeleg} shade />

    <path d="M17 14h25v3h4v8h-6v-2H19v2h-4v-9h2Z" fill="#372b22" />
    <path d="M18 15h26v3H18Z" fill="#ead7ae" />
    <path d="M21 17h18v2H21Z" fill="#fff0cc" />
    <path d="M21 19h4v5h-4Zm7 0h4v5h-4Zm7 0h4v5h-4Z" fill="#bda77f" />
    <path d="M24 20h4v5h-4Zm7 0h4v5h-4Z" fill="#241e1b" />
    <path d="M40 17h6v8h-6Z" fill="#d7bf92" />
    <path d="M42 19h3v4h-3Z" fill="#fff0cb" />
    <path d="M17 20h4v4h-4Z" fill="#d0ba8d" />

    <Bone d={pose.nearHindleg} />
    <Bone d={pose.nearForeleg} />
    <path d="M3 31h8v3H3Zm22 0h9v3h-9Zm24 0h8v3h-8Z" fill="#e7d2a3" opacity=".82" />

    <path d="M4 12h10v2h4v8h-2v3H5v-2H2v-7h2Z" fill="#30251d" />
    <path d="M5 13h9v2h3v6h-3v2H5v-2H3v-4h2Z" fill="#e8d4a5" />
    <path d="M7 13h5v2H7Zm-2 4h8v2H5Z" fill="#fff0cb" />
    <path d="M13 9h4v2h2v6h-3v-3h-3Z" fill="#352920" />
    <path d="M14 11h2v4h-2Z" fill="#d3bd92" />
    <rect x="8" y="17" width="3" height="3" fill="#211b18" />
    <rect x="9" y="17" width="1" height="1" fill="#a37658" />
    <rect x="2" y="19" width="4" height="3" fill="#251d18" />
    <path d="M6 23h11v4H7v-2H6Z" fill="#372a20" />
    <path d="M7 23h9v2H7Z" fill="#d4bb8a" />
    <rect x="9" y="25" width="2" height="2" fill="#f8e9bd" /><rect x="13" y="25" width="2" height="2" fill="#f8e9bd" />
  </g>
}

function KnockedDownHound() {
  return <g>
    <Bone d="M43 26 49 29 55 29" shade />
    <Bone d="M39 28 46 34 53 34" shade />
    <Bone d="M22 28 17 34 11 34" shade />
    <path d="M17 24h26v3h3v6H18v-2h-3v-5h2Z" fill="#332820" />
    <path d="M18 25h25v3H18Z" fill="#e8d4a5" />
    <path d="M22 28h5v4h-5Zm8 0h5v4h-5Zm8 0h4v4h-4Z" fill="#c3aa7d" />
    <path d="M26 29h4v4h-4Zm8 0h4v4h-4Z" fill="#211b18" />
    <Bone d="M21 29 25 35 30 35" />
    <Bone d="M39 29 34 35 28 35" />
    <path d="M4 23h11v2h4v7H5v-2H2v-5h2Z" fill="#30251d" />
    <path d="M5 24h10v2h3v4H5v-2H3v-2h2Z" fill="#e9d6ac" />
    <path d="M7 24h6v2H7Zm-2 3h8v2H5Z" fill="#fff0cb" />
    <path d="M13 20h4v3h2v5h-3v-3h-3Z" fill="#352920" />
    <path d="M14 22h2v3h-2Z" fill="#d3bd92" />
    <rect x="8" y="27" width="3" height="2" fill="#211b18" />
    <rect x="2" y="28" width="3" height="2" fill="#251d18" />
    <path d="M6 31h12v3H7v-2H6Z" fill="#372a20" />
    <path d="M8 31h9v1H8Z" fill="#d4bb8a" />
    <path d="M5 35h45v2H5Z" fill="#211b18" opacity=".28" />
  </g>
}
