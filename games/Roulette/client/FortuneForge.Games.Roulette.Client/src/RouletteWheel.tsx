import { useId } from 'react'
import { pocketColor } from './rouletteHelpers'
import walnutTexture from './assets/roulette-walnut-lacquer-v1.webp?no-inline'
import './rouletteWheel.css'

// Fixed European wheel order. A resting ball only depicts an already revealed server result.
const wheelOrder = [0, 32, 15, 19, 4, 21, 2, 25, 17, 34, 6, 27, 13, 36, 11, 30, 8, 23, 10, 5, 24, 16, 33, 1, 20, 14, 31, 9, 22, 18, 29, 7, 28, 12, 35, 3, 26] as const
const sector = 360 / wheelOrder.length
const point = (radius: number, angle: number) => {
  const radians = angle * Math.PI / 180
  return [radius * Math.cos(radians), radius * Math.sin(radians)] as const
}
const coordinate = ([x, y]: readonly [number, number]) => `${x.toFixed(3)} ${y.toFixed(3)}`
function ringSegment(inner: number, outer: number, from: number, to: number) {
  return `M ${coordinate(point(outer, from))} A ${outer} ${outer} 0 0 1 ${coordinate(point(outer, to))} L ${coordinate(point(inner, to))} A ${inner} ${inner} 0 0 0 ${coordinate(point(inner, from))} Z`
}

export function RouletteWheel({ spinning, settledPocket = null }: Readonly<{ spinning: boolean; settledPocket?: number | null }>) {
  const prefix = 'roulette-wheel-' + useId().replace(/:/g, '')
  const fill = (name: string) => `url(#${prefix}-${name})`
  const settledIndex = settledPocket === null ? -1 : wheelOrder.findIndex(number => number === settledPocket)
  const settledAngle = settledIndex < 0 ? null : -90 + settledIndex * sector
  // The stopped rotor has its original orientation. Radius 123.5 is inside the 112–135 ball pocket ring.
  const settledBall = !spinning && settledAngle !== null ? point(123.5, settledAngle) : null
  return <svg className={'ff-roulette-physical-wheel' + (spinning ? ' is-spinning' : '')} viewBox="0 0 560 560" aria-hidden="true" focusable="false">
    <defs>
      <radialGradient id={prefix + '-rim'} cx="43%" cy="26%" r="77%"><stop offset="0" stopColor="#343731" /><stop offset=".71" stopColor="#141713" /><stop offset=".83" stopColor="#070a08" /><stop offset=".9" stopColor="#292d29" /><stop offset=".93" stopColor="#090c0a" /><stop offset=".972" stopColor="#111712" /><stop offset="1" stopColor="#020503" /></radialGradient>
      <linearGradient id={prefix + '-rim-edge'} x1=".2" y1="0" x2=".8" y2="1"><stop offset="0" stopColor="#82847b" /><stop offset=".13" stopColor="#171d18" /><stop offset=".45" stopColor="#070c09" /><stop offset=".71" stopColor="#565c54" /><stop offset=".8" stopColor="#070d09" /><stop offset="1" stopColor="#151d16" /></linearGradient>
      <radialGradient id={prefix + '-wood'} cx="35%" cy="24%" r="79%"><stop offset="0" stopColor="#e8934f" /><stop offset=".34" stopColor="#bd642d" /><stop offset=".61" stopColor="#a34c23" /><stop offset=".85" stopColor="#d87536" /><stop offset=".96" stopColor="#75361b" /><stop offset="1" stopColor="#30150b" /></radialGradient>
      <radialGradient id={prefix + '-wood-inner'} cx="34%" cy="18%" r="86%"><stop offset="0" stopColor="#dc8845" /><stop offset=".48" stopColor="#af582a" /><stop offset=".81" stopColor="#c46d32" /><stop offset="1" stopColor="#733619" /></radialGradient>
      <radialGradient id={prefix + '-bowl-shade'}><stop offset=".54" stopColor="#170a03" stopOpacity=".03" /><stop offset=".73" stopColor="#190d08" stopOpacity=".09" /><stop offset=".91" stopColor="#000000" stopOpacity=".3" /><stop offset="1" stopColor="#000000" stopOpacity=".76" /></radialGradient>
      <linearGradient id={prefix + '-steel'}><stop offset="0" stopColor="#535a55" /><stop offset=".16" stopColor="#edf0e7" /><stop offset=".32" stopColor="#929c95" /><stop offset=".49" stopColor="#f7f9ed" /><stop offset=".64" stopColor="#69756d" /><stop offset=".83" stopColor="#d3d8cd" /><stop offset="1" stopColor="#424d44" /></linearGradient>
      <linearGradient id={prefix + '-red'} x1="0" y1="0" x2="1" y2="1"><stop stopColor="#bd4a40" /><stop offset=".6" stopColor="#9f2c28" /><stop offset="1" stopColor="#6d1a19" /></linearGradient>
      <linearGradient id={prefix + '-black'} x1="0" y1="0" x2="1" y2="1"><stop stopColor="#27302a" /><stop offset=".56" stopColor="#0d1410" /><stop offset="1" stopColor="#050b07" /></linearGradient>
      <linearGradient id={prefix + '-green'} x1="0" y1="0" x2="1" y2="1"><stop stopColor="#5ca86d" /><stop offset=".57" stopColor="#27734a" /><stop offset="1" stopColor="#174b32" /></linearGradient>
      <radialGradient id={prefix + '-ball'} cx="31%" cy="25%"><stop stopColor="#ffffff" /><stop offset=".4" stopColor="#f6f4dc" /><stop offset=".77" stopColor="#d4d1bc" /><stop offset="1" stopColor="#979d8b" /></radialGradient>
      <linearGradient id={prefix + '-spindle'}><stop stopColor="#222d25" /><stop offset=".12" stopColor="#6f7c70" /><stop offset=".28" stopColor="#eff5e6" /><stop offset=".4" stopColor="#a9b6a8" /><stop offset=".58" stopColor="#2c392e" /><stop offset=".82" stopColor="#101a12" /><stop offset="1" stopColor="#6b7667" /></linearGradient>
      <linearGradient id={prefix + '-reflection'} x1="0" y1="0" x2="1" y2="1"><stop stopColor="#ffffff" stopOpacity=".27" /><stop offset=".44" stopColor="#ffffff" stopOpacity=".02" /><stop offset=".61" stopColor="#ffffff" stopOpacity="0" /><stop offset="1" stopColor="#ffffff" stopOpacity=".07" /></linearGradient>
      <clipPath id={prefix + '-wood-clip'}><circle r="207" /></clipPath>
      <clipPath id={prefix + '-inner-clip'}><circle r="112" /></clipPath>
    </defs>
    <ellipse cx="280" cy="351" rx="245" ry="181" fill="#000" opacity=".23" />
    <ellipse cx="280" cy="318" rx="256" ry="219" fill="#050a07" />
    <ellipse cx="280" cy="309" rx="257" ry="223" fill={fill('rim-edge')} />
    <ellipse cx="280" cy="303" rx="257" ry="225" fill="#050906" />
    <g transform="translate(280 270) scale(1 .92)">
      <circle r="258" fill={fill('rim-edge')} />
      <circle r="255" fill={fill('rim')} stroke="#101710" strokeWidth="3" />
      <circle r="249" fill="none" stroke="#909287" strokeOpacity=".4" strokeWidth="1.2" />
      <circle r="239" fill="none" stroke="#020602" strokeWidth="7" />
      <circle r="235" fill="none" stroke="#606557" strokeOpacity=".34" strokeWidth="1" />
      <circle r="221" fill="#080e09" stroke="#151c15" strokeWidth="10" />
      <circle r="212" fill={fill('steel')} />
      <circle r="209" fill="#603a24" />
      <circle r="207" fill={fill('wood')} /><image href={walnutTexture} x="-207" y="-207" width="414" height="414" clipPath={fill('wood-clip')} opacity=".83" preserveAspectRatio="xMidYMid slice" />
      <g clipPath={fill('wood-clip')} fill="none" stroke="#5b260f" strokeWidth=".9" opacity=".09">
        {Array.from({ length: 23 }, (_, index) => <path key={index} d={`M ${-238 + index * 21} -244 C ${-303 + index * 25} -130, ${-123 + index * 13} -155, ${-191 + index * 18} -43 S ${-185 + index * 26} 123, ${-210 + index * 22} 246`} />)}
      </g>
      <circle r="205" fill={fill('bowl-shade')} />
      {[0, 45, 90, 135, 180, 225, 270, 315].map(angle => <g key={angle} transform={`rotate(${angle}) translate(0 -183)`}><path d="M -4 9 Q -7 -3 0 -12 Q 7 -3 4 9 Z" fill="#391d11" opacity=".5" transform="translate(2 3)" /><path d="M -4 9 Q -7 -3 0 -12 Q 7 -3 4 9 Z" fill={fill('steel')} stroke="#758172" strokeWidth=".5" /><path d="M 0 -10 L -2 5" stroke="#f3f2df" strokeWidth="1.1" opacity=".72" /></g>)}
      <circle r="157" fill="#111811" stroke="#bcbcab" strokeWidth="1.6" />
      <g className="ff-roulette-wheel-rotor">
        <circle r="154" fill={fill('steel')} />
        {wheelOrder.map((number, index) => {
          const angle = -90 + index * sector
          const start = angle - sector / 2
          const end = angle + sector / 2
          const [x, y] = point(144, angle)
          return <g key={number} data-wheel-pocket={number}>
            <path d={ringSegment(135, 153, start, end)} fill={fill(pocketColor(number))} stroke="#ced0bd" strokeWidth=".65" />
            <path d={ringSegment(112, 135, start, end)} fill={fill(pocketColor(number))} stroke="#aeb7a6" strokeWidth=".8" />
            <path d={`M ${coordinate(point(114, start))} L ${coordinate(point(134, start))}`} stroke="#f0edda" strokeWidth="1.7" />
            <text x={x} y={y} transform={`rotate(${angle + 90} ${x} ${y})`} dy=".34em" textAnchor="middle" fill="#faf4df" stroke="#0006" strokeWidth=".6" paintOrder="stroke" fontFamily="Arial, sans-serif" fontWeight="700" fontSize="16.4" letterSpacing="-.8">{number}</text>
          </g>
        })}
        <circle r="135" fill="none" stroke="#e8e7d3" strokeWidth="1.3" />
        <circle r="112" fill={fill('wood-inner')} stroke="#c7cabc" strokeWidth="1.8" /><image href={walnutTexture} x="-112" y="-112" width="224" height="224" clipPath={fill('inner-clip')} opacity=".76" preserveAspectRatio="xMidYMid slice" />
        <g clipPath={fill('inner-clip')} fill="none" stroke="#5c2b1b" strokeWidth=".75" opacity=".075">{Array.from({ length: 18 }, (_, index) => <path key={index} d={`M ${-140 + index * 17} -120 C ${-170 + index * 17} -20, ${-52 + index * 10} -36, ${-148 + index * 18} 25 S ${-127 + index * 17} 106, ${-85 + index * 18} 128`} />)}</g>
        {Array.from({ length: 12 }, (_, index) => <path key={index} d={`M ${coordinate(point(29, index * 30))} L ${coordinate(point(111, index * 30))}`} stroke="#4f2d1e" strokeOpacity=".32" strokeWidth=".85" />)}
        <circle r="111" fill={fill('reflection')} />
      </g>
      {spinning && <g className="ff-roulette-wheel-ball-track" data-wheel-ball="spinning"><circle cx="128" cy="-153" r="7.5" fill="#000" opacity=".37" transform="translate(2 3)" /><circle cx="128" cy="-153" r="7" fill={fill('ball')} stroke="#d5d8c9" strokeWidth=".6" /></g>}
      {settledBall && <g className="ff-roulette-wheel-settled-ball" data-wheel-ball="settled" data-settled-pocket={settledPocket} data-wheel-ball-angle={settledAngle}><circle cx={settledBall[0]} cy={settledBall[1]} r="7.5" fill="#000" opacity=".42" transform="translate(1.5 2.5)" /><circle className="ff-roulette-wheel-ball" cx={settledBall[0]} cy={settledBall[1]} r="7" fill={fill('ball')} stroke="#e3e1ce" strokeWidth=".6" /></g>}
      <circle r="208" fill="none" stroke="#ead4ae" strokeOpacity=".46" strokeWidth="1" />
      <path d="M -236 -65 A 245 245 0 0 1 85 -229" fill="none" stroke="#e4e4d9" strokeWidth="2.2" strokeOpacity=".46" />
      <path d="M -224 -100 A 246 246 0 0 1 -79 -233" fill="none" stroke="#fffff2" strokeWidth=".8" strokeOpacity=".58" />
      <path d="M -225 103 A 249 249 0 0 0 201 146" fill="none" stroke="#c1c7b9" strokeWidth="1.1" strokeOpacity=".42" />
      <ellipse cx="8" cy="19" rx="26" ry="10" fill="#201209" opacity=".44" />
      <ellipse cy="9" rx="25" ry="20" fill="#171e16" />
      <ellipse cy="5" rx="25" ry="18" fill={fill('steel')} stroke="#434d3e" strokeWidth="1.2" />
      <path d="M -18 -1 L -14 -13 L 14 -13 L 18 -1 Q 23 6 0 10 Q -23 6 -18 -1" fill={fill('spindle')} stroke="#334032" />
      <ellipse cy="-12" rx="15" ry="8" fill={fill('steel')} />
      <path d="M -11 -12 L -9 -46 Q 0 -51 9 -46 L 11 -12 Q 0 -5 -11 -12" fill={fill('spindle')} stroke="#404c3d" strokeWidth=".9" />
      <ellipse cy="-45" rx="10" ry="6" fill={fill('steel')} />
      <ellipse cy="-49" rx="9" ry="7" fill={fill('spindle')} />
      <ellipse cx="-2" cy="-51" rx="5.3" ry="2.8" fill="#f3f5e4" opacity=".94" />
      <path d="M -7 -43 L -8 -16" stroke="#f0f5e5" strokeWidth="1.2" strokeOpacity=".72" />
    </g>
  </svg>
}
