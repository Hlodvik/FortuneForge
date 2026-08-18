type SlotIconOptions = {
  label: string
  glyph: string
  background: string
  backgroundDeep: string
  rim: string
  glow: string
}

type SlotBackdropOptions = {
  label: string
  motif: string
  skyTop: string
  skyBottom: string
  horizon: string
  accent: string
  ground: string
}

function svgDataUri(svg: string): string {
  return `data:image/svg+xml;charset=UTF-8,${encodeURIComponent(svg)}`
}

export function createSlotIconSvg({
  label,
  glyph,
  background,
  backgroundDeep,
  rim,
  glow,
}: SlotIconOptions): string {
  return svgDataUri(`
    <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 512 512" role="img" aria-label="${label}">
      <title>${label}</title>
      <defs>
        <radialGradient id="face" cx="34%" cy="25%" r="78%">
          <stop stop-color="${background}"/>
          <stop offset="1" stop-color="${backgroundDeep}"/>
        </radialGradient>
        <linearGradient id="ring" x1="0" y1="0" x2="1" y2="1">
          <stop stop-color="#fffbd1"/>
          <stop offset=".27" stop-color="${rim}"/>
          <stop offset=".7" stop-color="${glow}"/>
          <stop offset="1" stop-color="#fff2a3"/>
        </linearGradient>
        <filter id="shadow" x="-30%" y="-30%" width="160%" height="170%">
          <feDropShadow dx="0" dy="14" stdDeviation="14" flood-color="#03020d" flood-opacity=".62"/>
          <feDropShadow dx="0" dy="0" stdDeviation="10" flood-color="${glow}" flood-opacity=".42"/>
        </filter>
      </defs>
      <g filter="url(#shadow)">
        <circle cx="256" cy="256" r="218" fill="url(#ring)"/>
        <circle cx="256" cy="256" r="190" fill="url(#face)" stroke="#fffbd1" stroke-width="7"/>
        <path d="M116 157 Q256 74 396 157" fill="none" stroke="#fff" stroke-opacity=".3" stroke-width="15" stroke-linecap="round"/>
        <text x="256" y="326" text-anchor="middle" font-family="Segoe UI Emoji, Apple Color Emoji, Noto Color Emoji, sans-serif" font-size="208">${glyph}</text>
        <circle cx="256" cy="50" r="22" fill="${rim}" stroke="#fffbd1" stroke-width="7"/>
      </g>
    </svg>
  `)
}

export function createSlotBackdropSvg({
  label,
  motif,
  skyTop,
  skyBottom,
  horizon,
  accent,
  ground,
}: SlotBackdropOptions): string {
  return svgDataUri(`
    <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 1600 1000" role="img" aria-label="${label}" preserveAspectRatio="xMidYMid slice">
      <title>${label}</title>
      <defs>
        <linearGradient id="sky" x1="0" y1="0" x2="0" y2="1">
          <stop stop-color="${skyTop}"/>
          <stop offset=".66" stop-color="${skyBottom}"/>
          <stop offset="1" stop-color="${ground}"/>
        </linearGradient>
        <radialGradient id="light" cx="50%" cy="35%" r="48%">
          <stop stop-color="${accent}" stop-opacity=".48"/>
          <stop offset="1" stop-color="${accent}" stop-opacity="0"/>
        </radialGradient>
        <pattern id="stars" width="126" height="114" patternUnits="userSpaceOnUse">
          <circle cx="18" cy="24" r="2.6" fill="#fff" fill-opacity=".72"/>
          <circle cx="91" cy="68" r="1.8" fill="${accent}" fill-opacity=".78"/>
          <circle cx="54" cy="103" r="1.2" fill="#fff" fill-opacity=".58"/>
        </pattern>
        <filter id="soft"><feGaussianBlur stdDeviation="16"/></filter>
      </defs>
      <rect width="1600" height="1000" fill="url(#sky)"/>
      <rect width="1600" height="1000" fill="url(#light)"/>
      <rect width="1600" height="760" fill="url(#stars)"/>
      <circle cx="800" cy="355" r="220" fill="${accent}" fill-opacity=".12" filter="url(#soft)"/>
      <text x="800" y="465" text-anchor="middle" font-family="Segoe UI Emoji, Apple Color Emoji, Noto Color Emoji, sans-serif" font-size="310" opacity=".3">${motif}</text>
      <path d="M0 760 Q230 620 430 735 T830 700 T1220 730 T1600 650 V1000 H0Z" fill="${horizon}" fill-opacity=".78"/>
      <path d="M0 835 Q260 700 520 820 T1040 790 T1600 745 V1000 H0Z" fill="${ground}" fill-opacity=".94"/>
      <path d="M0 875 Q300 790 600 865 T1200 835 T1600 800" fill="none" stroke="${accent}" stroke-opacity=".22" stroke-width="9"/>
      <rect x="18" y="18" width="1564" height="964" rx="42" fill="none" stroke="${accent}" stroke-opacity=".2" stroke-width="6"/>
    </svg>
  `)
}
