// FortuneForge original composition and sound-design renderer. No recordings,
// third-party melodies, runtime synthesis, network, dependencies, or accounts.
// Neon Nights is a transparent transcode of the separately approved WAV masters.
import { mkdir, readFile, writeFile } from 'node:fs/promises'
import { dirname, relative, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'
import { createHash } from 'node:crypto'
import { spawnSync } from 'node:child_process'

const REPO = resolve(dirname(fileURLToPath(import.meta.url)), '../..')
const OUTPUT = resolve(REPO, 'output/artwork/themed-slots-audio-2026-11-02')
const PUBLIC = resolve(REPO, 'fortuneforge.client/public/assets/slots/audio-suites/v1')
const NEON = resolve(REPO, 'output/artwork/neon-nights-2026-11-02/audio-suite-v1')
const FFMPEG = process.env.FF_AUDIO_FFMPEG || resolve(REPO, 'output/maintenance-2026-11-02/midnight-vault-audio-refresh/reference-9L-pYOQqd4M/runtime/imageio_ffmpeg/binaries/ffmpeg-win-x86_64-v9.0.2.exe')
const SR = 44100, TAU = 2 * Math.PI
const hash = bytes => createHash('sha256').update(bytes).digest('hex')
const pathInRepo = path => relative(REPO, path).replaceAll('\\', '/')
const db = n => +(20 * Math.log10(Math.max(1e-9, n))).toFixed(3)
const hz = midi => 440 * 2 ** ((midi - 69) / 12)
const mono = seconds => new Float32Array(Math.ceil(SR * seconds))
const stereo = seconds => [mono(seconds), mono(seconds)]
let seed = 1
const cache = new Map()
function random() { seed ^= seed << 13; seed ^= seed >>> 17; seed ^= seed << 5; return (seed >>> 0) / 4294967296 }

// Instrument names describe original synthesized timbres, not sampled acoustic
// instruments. Partial ratios, excitation, envelopes and modulation distinguish
// the physical-inspired instruments from the electronic sound-design kernels.
function voice(midi, seconds, kind = 'piano') {
  const key = `${midi}/${seconds.toFixed(5)}/${kind}`
  if (cache.has(key)) return cache.get(key)
  const out = mono(seconds), f = hz(midi)
  for (let i = 0; i < out.length; i++) {
    const t = i / SR, p = TAU * f * t
    const edge = Math.min(1, t / .006, (seconds - t) / .07)
    let v, env
    switch (kind) {
      case 'harp': v = Math.sin(p) + .36 * Math.sin(2 * p) * Math.exp(-5*t) + .16*Math.sin(3*p)*Math.exp(-8*t); env = Math.exp(-3.4*t); break
      case 'lyre': v = Math.sin(p) + .30 * Math.sin(2 * p + .4) + .12*Math.sin(4*p); env = Math.exp(-4.0*t); break
      case 'koto': v = Math.sin(p + .7*Math.exp(-30*t)*Math.sin(p*2)) + .4*Math.sin(2.002*p)*Math.exp(-7*t) + .18*Math.sin(4.008*p)*Math.exp(-13*t); env = Math.exp(-3.8*t); break
      case 'oud': v = Math.sin(p) + .5*Math.sin(2*p)*Math.exp(-2*t) + .24*Math.sin(3*p)*Math.exp(-5*t) + .13*Math.sin(5*p)*Math.exp(-11*t); env = Math.exp(-4.8*t); break
      case 'twang': { const bend = p - .065*f/25*(1-Math.exp(-25*t)); v = Math.sin(bend) + .44*Math.sin(2*bend)*Math.exp(-5*t) + .21*Math.sin(3*bend)*Math.exp(-8*t); env = Math.exp(-3.0*t); break }
      case 'celesta': v = Math.sin(p) + .44*Math.sin(2.758*p)*Math.exp(-4.2*t) + .15*Math.sin(5.406*p)*Math.exp(-8*t); env = Math.exp(-3.2*t); break
      case 'glass': v = Math.sin(p) + .28*Math.sin(2.01*p)*Math.exp(-2.4*t) + .13*Math.sin(3.98*p)*Math.exp(-5*t); env = Math.exp(-2.5*t); break
      case 'marimba': v = Math.sin(p) + .44*Math.sin(4.01*p)*Math.exp(-14*t) + .10*Math.sin(9.98*p)*Math.exp(-22*t); env = Math.exp(-5.8*t); break
      case 'wood': v = Math.sin(p) + .32*Math.sin(2.73*p)*Math.exp(-12*t) + .16*Math.sin(5.84*p)*Math.exp(-18*t); env = Math.exp(-7*t); break
      case 'piano': v = Math.sin(p) + .36*Math.sin(2.001*p)*Math.exp(-2*t) + .16*Math.sin(3.006*p)*Math.exp(-5*t) + .08*Math.sin(4.012*p)*Math.exp(-8*t); env = Math.exp(-2.8*t); break
      case 'harpsichord': v = Math.sin(p) + .42*Math.sin(2*p) + .22*Math.sin(3*p) + .12*Math.sin(5*p); env = Math.exp(-4.1*t); break
      case 'organ': v = .64*Math.sin(p)+.28*Math.sin(p*2)+.21*Math.sin(p*3)+.08*Math.sin(p*6); env = Math.min(1,t/.035)*Math.min(1,(seconds-t)/.18)*(.9+.05*Math.sin(TAU*4.7*t)); break
      case 'choir': v = Math.sin(p + .025*Math.sin(TAU*4.8*t)) + .16*Math.sin(2*p) + .12*Math.sin(3*p); env = Math.min(1,t/.25)*Math.min(1,(seconds-t)/.35)*.65; break
      case 'strings': v = Math.sin(p + .04*Math.sin(TAU*4.2*t)) + .23*Math.sin(2*p)+.10*Math.sin(3*p); env = Math.min(1,t/.16)*Math.min(1,(seconds-t)/.28)*.72; break
      case 'flute': v = Math.sin(p+.035*Math.sin(TAU*5.2*t))+.12*Math.sin(2*p); env = Math.min(1,t/.07)*Math.min(1,(seconds-t)/.14)*Math.exp(-.5*t); break
      case 'harmonica': v = Math.sin(p+.045*Math.sin(TAU*5*t))+.32*Math.sin(2*p)+.14*Math.sin(3*p); env = Math.min(1,t/.055)*Math.min(1,(seconds-t)/.16)*Math.exp(-.8*t); break
      case 'horn': v = Math.sin(p+.015*Math.sin(TAU*4.2*t))+.34*Math.sin(2*p)+.14*Math.sin(3*p)+.08*Math.sin(4*p); env = Math.min(1,t/.11)*Math.min(1,(seconds-t)/.22)*Math.exp(-.5*t); break
      case 'bronze': v = Math.sin(p)+.44*Math.sin(p*2.71)*Math.exp(-3*t)+.22*Math.sin(p*4.09)*Math.exp(-5*t)+.08*Math.sin(p*6.8)*Math.exp(-8*t); env = Math.exp(-3*t); break
      case 'fm': v = Math.sin(p + (1.2*Math.exp(-7*t)+.25)*Math.sin(2*p)); env = Math.exp(-2.3*t); break
      case 'robot': v = Math.sin(p + (2.6*Math.exp(-8*t)+.45)*Math.sin(2*p))+.16*Math.sin(p*4); env = Math.exp(-3.5*t); break
      case 'synthpad': v = Math.sin(p)+.26*Math.sin(p*1.002)+.14*Math.sin(2*p); env = Math.min(1,t/.19)*Math.min(1,(seconds-t)/.3)*.74; break
      case 'sub': v = Math.sin(p)+.18*Math.sin(2*p)*Math.exp(-9*t); env = Math.exp(-4.5*t); break
      default: throw Error(`Unknown timbre ${kind}`)
    }
    out[i] = v * env * Math.max(0,edge) * .72
  }
  cache.set(key,out)
  return out
}
function noise(seconds, cutoff = 3600) {
  const out = mono(seconds), a = 1-Math.exp(-TAU*cutoff/SR)
  let low = 0, high = 0
  for (let i=0; i<out.length; i++) { low += a*(random()*2-1-low); high += .025*(low-high); out[i] = low-high }
  return out
}
function percussion(kind = 'hand', pitch = 0) {
  const key=`drum/${kind}/${pitch}`
  if(cache.has(key)) return cache.get(key)
  const seconds = kind==='taiko' || kind==='low' ? .7 : kind==='brush' ? .2 : .23
  const out=mono(seconds), raw=noise(seconds,kind==='shaker'?10000:kind==='brush'?5800:2800)
  for(let i=0;i<out.length;i++) {
    const t=i/SR, attack=Math.min(1,t/.0015)
    if(kind==='shaker' || kind==='brush') out[i]=raw[i]*Math.exp(-t*(kind==='brush'?26:45))*attack*.6
    else { const f=kind==='taiko'?61:kind==='low'?70:kind==='darbuka'?156:kind==='electro'?54:kind==='wood'?440:205
      const p=TAU*((f+pitch)*t+1.2*(1-Math.exp(-28*t)))
      out[i]=(Math.sin(p)*Math.exp(-t*(kind==='taiko'?6:kind==='low'?9:kind==='wood'?42:19))+.16*Math.sin(p*1.48)*Math.exp(-29*t)+raw[i]*Math.exp(-65*t)*.22)*attack
    }
  }
  cache.set(key,out);return out
}
function add(track, signal, at, gain=1, pan=0, wrap=false) {
  const start=Math.round(at*SR), n=track[0].length, l=Math.cos((pan+1)*Math.PI/4)*gain, r=Math.sin((pan+1)*Math.PI/4)*gain
  for(let i=0;i<signal.length;i++){let to=start+i;if(wrap)to=((to%n)+n)%n;if(to<0||to>=n)continue;track[0][to]+=signal[i]*l;track[1][to]+=signal[i]*r}
}
function chord(track, notes, at, seconds, gain, kind, wrap=false) {
  notes.forEach((note,i)=>add(track,voice(note,seconds,kind),at+i*.002,gain/Math.sqrt(notes.length),(i/Math.max(1,notes.length-1)-.5)*.9,wrap))
}
function space(track, amount=.2, wrap=false) {
  const dry=track.map(a=>a.slice()), n=track[0].length
  for(const [tap,[time,gain]] of [[.031,.30],[.073,.23],[.127,.17],[.211,.12],[.347,.07]].entries()) {
    const shift=Math.round(time*SR)
    for(let c=0;c<2;c++)for(let i=0;i<n;i++){const from=i-shift;if(from>=0||wrap)track[c][i]+=dry[tap%2?1-c:c][(from+n)%n]*gain*amount}
  }
  return track
}
function master(track,{rms=.12,peak=.72,loop=false}={}) {
  let maximum=0,sum=0
  for(const data of track){let mean=0;for(const v of data){if(!Number.isFinite(v))throw Error('Non-finite source sample');mean+=v}mean/=data.length;for(let i=0;i<data.length;i++){data[i]=Math.tanh((data[i]-mean)*1.1);maximum=Math.max(maximum,Math.abs(data[i]));sum+=data[i]**2}}
  const scale=Math.min(peak/Math.max(.001,maximum),rms/Math.max(.0001,Math.sqrt(sum/(track[0].length*2))))
  const fade=Math.round(SR*(loop?.002:.004))
  for(const data of track)for(let i=0;i<data.length;i++)data[i]*=scale*Math.min(1,i/fade,(data.length-1-i)/fade)
  return track
}
function wav(track) {
  const frames=track[0].length,out=Buffer.alloc(44+frames*4)
  out.write('RIFF',0);out.writeUInt32LE(out.length-8,4);out.write('WAVEfmt ',8);out.writeUInt32LE(16,16);out.writeUInt16LE(1,20);out.writeUInt16LE(2,22);out.writeUInt32LE(SR,24);out.writeUInt32LE(SR*4,28);out.writeUInt16LE(4,32);out.writeUInt16LE(16,34);out.write('data',36);out.writeUInt32LE(frames*4,40)
  for(let i=0;i<frames;i++)for(let c=0;c<2;c++)out.writeInt16LE(Math.round(Math.max(-1,Math.min(1,track[c][i]))*32767),44+i*4+c*2)
  return out
}
function wavStats(bytes) {
  if(bytes.toString('ascii',0,4)!=='RIFF'||bytes.readUInt16LE(22)!==2||bytes.readUInt32LE(24)!==SR||bytes.readUInt16LE(34)!==16)throw Error('Unexpected WAV format')
  let max=0,sum=0
  for(let p=44;p<bytes.length;p+=2){const n=bytes.readInt16LE(p)/32768;max=Math.max(max,Math.abs(n));sum+=n*n}
  return {durationSeconds:(bytes.length-44)/(SR*4),peakDbfs:db(max),rmsDbfs:db(Math.sqrt(sum/((bytes.length-44)/2))),sampleRate:SR,channels:2,frames:(bytes.length-44)/4}
}

const MAJOR=[0,2,4,5,7,9,11], MINOR=[0,2,3,5,7,8,10], PENTA=[0,2,4,7,9], EAST=[0,1,5,7,8], DORIAN=[0,2,3,5,7,9,10], DESERT=[0,1,4,5,7,8,10]
// Each title has its own key, scale, harmony, melody, pulse, meter and instrumental
// roles. Music shares only reusable mixing / synthesis code across the titles.
export const THEMES=[
  {slug:'rainbow-realm',title:'Rainbow Realm',root:60,bpm:105,scale:[0,2,4,6,7,9,11],harmony:[0,4,1,3],motif:[0,2,4,6,4,2,1,4],lead:'celesta',comp:'glass',pad:'flute',drum:'hand',rhythm:[0,1.5,2.5,3.5],feel:'Luminous celesta, glass bells and a floating flute; bright Lydian garden dance.'},
  {slug:'gods-of-olympus',title:'Gods of Olympus',root:55,bpm:102,scale:MAJOR,harmony:[0,3,5,4],motif:[4,2,0,1,4,6,5,4],lead:'harp',comp:'bronze',pad:'horn',drum:'low',rhythm:[0,.75,2,3],feel:'Harp arpeggios, restrained bronze chimes, ceremonial low drums and warm horn harmony.'},
  {slug:'reel-riches',title:'Reel Riches',root:58,bpm:114,scale:MAJOR,harmony:[0,5,1,4],motif:[2,4,6,5,4,1,2,0],lead:'piano',comp:'piano',pad:'strings',drum:'brush',rhythm:[0,1.66,2.5,3.66],swing:.11,feel:'Original lounge-jazz piano, walking upright-inspired bass, brushes and soft strings.'},
  {slug:'high-noon-fortune',title:'High Noon Fortune',root:55,bpm:118,scale:PENTA,harmony:[0,3,0,4],motif:[0,2,1,3,4,3,1,0],lead:'twang',comp:'lyre',pad:'harmonica',drum:'wood',rhythm:[0,1,2.5,3.5],feel:'Twangy plucked strings, harmonica phrases and a dry wooden trail rhythm.'},
  {slug:'royal-draw',title:'Royal Draw',root:62,bpm:100,meter:3,scale:MAJOR,harmony:[0,5,3,4],motif:[0,4,2,5,4,1,3,2],lead:'piano',comp:'harpsichord',pad:'strings',drum:'brush',rhythm:[0,1,2],feel:'Elegant three-beat piano waltz with harpsichord ornaments and chamber strings.'},
  {slug:'arcane-archives',title:'Arcane Archives',root:57,bpm:90,scale:DORIAN,harmony:[0,3,1,4],motif:[0,3,2,5,4,2,6,4],lead:'celesta',comp:'harp',pad:'choir',drum:'hand',rhythm:[0,.5,2,3.5],feel:'Dorian celesta figures, quiet harp filigree and breathy, nonverbal choir tones.'},
  {slug:'cosmic-fortune',title:'Cosmic Fortune',root:54,bpm:104,scale:DORIAN,harmony:[0,5,3,1],motif:[4,6,2,5,1,4,3,0],lead:'fm',comp:'glass',pad:'synthpad',drum:'electro',rhythm:[0,.75,1.75,3],feel:'Weightless FM keys, glassy orbital arpeggios and a soft electronic pulse.'},
  {slug:'dino-dominion',title:'Dino Dominion',root:50,bpm:94,scale:DORIAN,harmony:[0,2,3,4],motif:[0,0,4,3,2,4,5,2],lead:'marimba',comp:'wood',pad:'horn',drum:'taiko',rhythm:[0,1.5,2.25,3.5],feel:'Low expedition drums, resonant marimba and distant horn calls.'},
  {slug:'jungle-jackpot',title:'Jungle Jackpot',root:60,bpm:112,scale:PENTA,harmony:[0,3,1,4],motif:[0,1,3,2,4,3,1,2],lead:'wood',comp:'marimba',pad:'flute',drum:'hand',rhythm:[0,.75,1.5,2.5,3.25],feel:'Interlocking wooden plucks, marimba, hand drums and canopy flute phrases.'},
  {slug:'ocean-odyssey',title:'Ocean Odyssey',root:62,bpm:92,scale:PENTA,harmony:[0,3,4,1],motif:[0,2,4,3,1,2,3,0],lead:'harp',comp:'glass',pad:'flute',drum:'brush',rhythm:[0,1.5,2.5],ambience:'wave',feel:'Airy harp, rounded glass notes, breathing flute and a subtle synthesized surf wash.'},
  {slug:'samurai-fortune',title:'Samurai Fortune',root:57,bpm:96,scale:EAST,harmony:[0,3,2,0],motif:[0,2,3,4,2,1,3,0],lead:'koto',comp:'koto',pad:'flute',drum:'taiko',rhythm:[0,1.5,2,3.75],feel:'Koto-inspired bent plucks, spare flute and low ceremonial drum patterns.'},
  {slug:'candy-carnival',title:'Candy Carnival',root:65,bpm:128,scale:MAJOR,harmony:[0,3,1,4],motif:[0,2,4,2,5,4,3,1],lead:'marimba',comp:'celesta',pad:'organ',drum:'wood',rhythm:[0,.5,1.5,2,3.5],feel:'Bouncing marimba, tiny celesta sparks and a soft fairground organ.'},
  {slug:'phantom-manor',title:'Phantom Manor',root:57,bpm:86,meter:3,scale:MINOR,harmony:[0,5,3,4],motif:[4,2,0,6,5,3,1,4],lead:'glass',comp:'harpsichord',pad:'organ',drum:'brush',rhythm:[0,1.5,2],feel:'A minor-key haunted waltz with low organ, music-box glass and dusty harpsichord.'},
  {slug:'nordic-legends',title:'Nordic Legends',root:50,bpm:92,scale:DORIAN,harmony:[0,3,5,4],motif:[0,4,3,2,0,5,4,2],lead:'horn',comp:'lyre',pad:'choir',drum:'low',rhythm:[0,1.5,2,3],feel:'Broad low horn melody, plucked lyre, wordless air and grounded frame-drum pulse.'},
  {slug:'desert-treasures',title:'Desert Treasures',root:57,bpm:108,scale:DESERT,harmony:[0,3,1,4],motif:[0,1,2,4,3,2,1,0],lead:'oud',comp:'oud',pad:'flute',drum:'darbuka',rhythm:[0,.75,1.5,2,2.75,3.5],feel:'Oud-inspired plucks, ornamented desert-scale flute and a dancing darbuka rhythm.'},
  {slug:'robot-revolution',title:'Robot Revolution',root:48,bpm:122,scale:DORIAN,harmony:[0,2,5,3],motif:[0,4,1,5,2,6,3,4],lead:'robot',comp:'fm',pad:'synthpad',drum:'electro',rhythm:[0,.75,1.5,2.75,3.5],feel:'Metallic FM circuits, rounded machine bass, bright digital stabs and a syncopated motor beat.'},
]
function degree(theme,degree,octave=0){const n=theme.scale.length,oct=Math.floor(degree/n);return theme.root+theme.scale[((degree%n)+n)%n]+12*(oct+octave)}
function harmony(theme,degreeIndex,octave=0){return [0,2,4].map(n=>degree(theme,degreeIndex+n,octave))}
function music(theme,bonus=false){
  const bpm=theme.bpm+(bonus?8:0),beat=60/bpm,meter=theme.meter||4,bars=16,out=stereo(bars*meter*beat)
  for(let bar=0;bar<bars;bar++){
    const at=bar*meter*beat,root=theme.harmony[Math.floor(bar/2)%4],notes=harmony(theme,root,-1)
    chord(out,notes,at,meter*beat+.18,.10,theme.pad,true)
    add(out,voice(degree(theme,root,-2),.65*beat,'sub'),at,.34,0,true)
    add(out,voice(degree(theme,root+4,-2),.60*beat,'sub'),at+(meter===3?2:2.5)*beat,.24,0,true)
    for(let i=0;i<theme.rhythm.length;i++){
      const offset=theme.rhythm[i], note=notes[(i+bar)%3]+(bonus?12:0)
      if(offset<meter)add(out,voice(note,.66,theme.comp),at+(offset+(theme.swing&&i%2?theme.swing:0))*beat,bonus?.13:.105,i%2?.32:-.32,true)
    }
    for(let b=0;b<meter;b++){
      if(b===0||b===2||bonus)add(out,percussion(theme.drum),at+b*beat,b===0?.29:.18,0,true)
      if(theme.drum!=='brush'&&(b===1||b===3))add(out,percussion(theme.drum,73),at+b*beat,.095,.22,true)
      add(out,percussion('shaker'),at+(b+.5)*beat,.065,b%2?.48:-.48,true)
      if(bonus)add(out,percussion('shaker'),at+(b+.25)*beat,.025,-.4,true)
    }
    // Repeated four-bar phrases have an answering second half and a restrained
    // eight-bar variation, rather than endless random scales.
    const phrase=bar%4, m=theme.motif
    if(phrase!==3||bonus){
      const indexes=phrase%2?[4,5,6]:[0,1,2,3], positions=phrase%2?[.5,1.5,2.5]:[0,.75,1.5,2.5]
      indexes.forEach((idx,j)=>{const pos=positions[j]*(meter/4);const note=degree(theme,m[(idx+(bar>=8?1:0))%m.length],bonus?1:0);add(out,voice(note,theme.lead==='horn'?1.1:.78,theme.lead),at+pos*beat,bonus?.16:.12,j%2?.18:-.18,true)})
    }else add(out,voice(degree(theme,m[7]),1.4,theme.lead),at+beat,.10,.15,true)
    if(bonus&&bar%4===3)chord(out,harmony(theme,root,0),at+(meter-.5)*beat,.5,.08,theme.comp,true)
  }
  if(theme.ambience==='wave'){
    const raw=noise(out[0].length/SR,780),n=raw.length
    for(let i=0;i<n;i++)raw[i]*=(.012+.016*(.5+.5*Math.sin(TAU*i/n*4)))
    add(out,raw,0,1,-.2,true)
  }
  return space(out,theme.pad==='organ'?.24:.35,true)
}
function click(theme,variant=false){
  const out=stereo(.12),raw=noise(.08,theme.drum==='electro'?6400:3800)
  for(let i=0;i<raw.length;i++){const t=i/SR;raw[i]=(raw[i]*.62+.2*Math.sin(TAU*(variant?310:230)*t))*Math.exp(-100*t)}
  add(out,raw,.001,.7);if(variant)add(out,raw,.023,.16,.15);return out
}
function rolling(theme,bonus=false){
  const out=stereo(1.6),raw=noise(1.6,bonus?2100:1450),rate=theme.drum==='electro'?25:theme.drum==='taiko'?18:22
  for(let i=0;i<raw.length;i++){const t=i/SR;raw[i]=raw[i]*(.45+.15*Math.cos(TAU*rate*t))+.016*Math.sin(TAU*(theme.root+33)*t)}
  add(out,raw,0,1);return out
}
function land(theme,bonus=false){
  const out=stereo(.17),raw=noise(.12,bonus?5300:4000)
  for(let i=0;i<raw.length;i++){const t=i/SR;raw[i]=raw[i]*Math.exp(-85*t)*.35+Math.sin(TAU*(bonus?240:180)*t)*Math.exp(-64*t)*.35}
  add(out,raw,.001,.8);return out
}
function launch(theme){
  const out=stereo(.42),raw=noise(.34,3600)
  for(let i=0;i<raw.length;i++){const t=i/SR;raw[i]*=Math.max(0,Math.sin(Math.PI*t/.34))**1.5*Math.exp(-4*t)*.2}
  add(out,raw,0,.7,-.15);add(out,click(theme)[0],0,.6);add(out,voice(theme.root-24,.27,'sub'),.02,.22);return out
}
function collect(theme,index){
  const out=stereo(.86),intervals=[[0,4],[2,6],[4,7],[1,5]][index]
  add(out,click(theme,index===1)[0],0,.21)
  intervals.forEach((d,i)=>add(out,voice(degree(theme,d,index===2?-1:0),.64,theme.lead),.02+i*(index===3?.10:.055),.34,i?.25:-.25))
  return space(out,.36)
}
function award(theme,tier=0,kind='win'){
  const duration=kind==='entry'?3.3:kind==='complete'?2.6:kind==='triple'?2.35:[1.3,2.05,3.15][tier],out=stereo(duration)
  const root=theme.root, winning=[root,root+4,root+7,root+12]
  const stages=kind==='entry'?[[.4,[root-5,root,root+4,root+7]],[.83,winning]]:kind==='complete'?[[0,[root-5,root+2,root+7]],[.38,winning]]:kind==='triple'?[[0,winning],[.18,winning],[.44,winning]]:tier===0?[[.02,winning]]:tier===1?[[0,[root-5,root,root+4]],[.30,winning]]:[[0,[root-5,root,root+4]],[.35,[root-5,root+2,root+7]],[.78,winning]]
  for(const [at,notes]of stages){chord(out,notes,at,tier===2?1.5:.85,.39,theme.comp);add(out,voice(root-24,.65,'sub'),at,.22);add(out,percussion(theme.drum),at,.15)}
  const last=stages.at(-1)[0]
  add(out,voice(root+16,1.0,theme.lead),last+.12,.18,.25)
  if(kind==='entry')for(const at of [.08,.27,.47])add(out,percussion(theme.drum),at,.10+at*.18)
  return space(out,.46)
}

const IDS=['night-drive','midnight-mix','button-click','bet-step','spin-launch','reel-roll','reel-land','bonus-reel-roll','bonus-reel-land','collect-flamingo','collect-dice','collect-sunset','collect-arcade','win-small','win-medium','win-big','triple-sevens','no-win','bonus-entry','bonus-complete']
const commonTracks=theme=>[
  ['night-drive','Base music','Music',()=>music(theme),{loop:true,volume:.62,rms:.135},`16-bar original base arrangement. ${theme.feel}`],
  ['midnight-mix','Bonus music','Music',()=>music(theme,true),{loop:true,volume:.60,rms:.14},`16-bar brighter bonus variation, eight BPM faster with additional rhythmic detail. ${theme.feel}`],
  ['button-click','Button click','Controls',()=>click(theme),{volume:.58,peak:.46,rms:.07},'Short, quiet tactile control detent.'],
  ['bet-step','Bet adjustment','Controls',()=>click(theme,true),{volume:.54,peak:.45,rms:.065},'Light two-part control detent.'],
  ['spin-launch','Spin launch','Controls',()=>launch(theme),{volume:.65,rms:.095},'Tactile press, filtered air and a soft low accent.'],
  ['reel-roll','Reels moving','Reels',()=>rolling(theme),{loop:true,volume:.40,rms:.065},'Restrained filtered roller texture, below the music.'],
  ['reel-land','Reel lands','Reels',()=>land(theme),{volume:.62,peak:.50,rms:.07},'Short landing transient for staggered reels.'],
  ['bonus-reel-roll','Bonus reels moving','Reels',()=>rolling(theme,true),{loop:true,volume:.40,rms:.07},'Slightly brighter bonus roller texture.'],
  ['bonus-reel-land','Bonus reel lands','Reels',()=>land(theme,true),{volume:.60,peak:.52,rms:.07},'Brighter compact bonus landing transient.'],
  ...['flamingo','dice','sunset','arcade'].map((name,index)=>[`collect-${name}`,`Collection track ${index+1}`,'Collections',()=>collect(theme,index),{volume:.58,peak:.62,rms:.105},`Distinct ${theme.lead} collection accent ${index+1}; filename is a shared runtime cue identifier, not a game symbol label.`]),
  ...['small','medium','big'].map((tier,index)=>[`win-${tier}`,`${tier[0].toUpperCase()+tier.slice(1)} win`,'Wins',()=>award(theme,index),{volume:index===2?.70:.65,rms:index===2?.14:.125},`One complete positive ${theme.comp}/${theme.lead} award stinger; ${index+1} harmonic stages.`]),
  ['triple-sevens','Triple / special symbol award','Wins',()=>award(theme,1,'triple'),{volume:.67,rms:.125},'Three compact themed accents with a single resolving tail.'],
  ['no-win','Neutral landing','Wins',()=>land(theme),{volume:.30,peak:.32,rms:.035},'Quiet optional neutral landing, with no failure melody.'],
  ['bonus-entry','Bonus awarded','Bonus',()=>award(theme,2,'entry'),{volume:.68,rms:.13},'Restrained themed percussion lift opening into one resolved musical award.'],
  ['bonus-complete','Bonus complete','Bonus',()=>award(theme,1,'complete'),{volume:.66,rms:.125},'Two-chord themed resolution for returning to base music.'],
]
function ffmpeg(args){const result=spawnSync(FFMPEG,args,{encoding:null,maxBuffer:128*1024*1024,windowsHide:true});if(result.status!==0)throw Error(`ffmpeg failed: ${result.stderr?.toString().slice(-1500)}`);return result.stdout}
async function encode(wavPath,oggPath,description){
  ffmpeg(['-hide_banner','-loglevel','error','-y','-i',wavPath,'-map_metadata','-1','-c:a','libvorbis','-q:a','5','-ar',String(SR),'-ac','2','-metadata',`comment=FortuneForge original synthesis. ${description}`,oggPath])
  return readFile(oggPath)
}
async function decodeCheck(path,expectedDuration){
  const pcm=ffmpeg(['-hide_banner','-loglevel','error','-i',path,'-f','f32le','-acodec','pcm_f32le','-ar',String(SR),'-ac','2','pipe:1'])
  const frames=pcm.length/8;let peak=0,sum=0
  for(let i=0;i<pcm.length;i+=4){const v=pcm.readFloatLE(i);if(!Number.isFinite(v))throw Error(`Non-finite PCM: ${path}`);peak=Math.max(peak,Math.abs(v));sum+=v*v}
  const duration=frames/SR
  if(peak>=.98||Math.abs(duration-expectedDuration)>.035||peak<.001)throw Error(`Audio bounds failed for ${path}: peak ${peak}, duration ${duration}`)
  return {durationSeconds:+duration.toFixed(6),decodedPeakDbfs:db(peak),decodedRmsDbfs:db(Math.sqrt(sum/(pcm.length/4))),decodeVerified:true}
}
async function save(theme,track,approved=false){
  const [id,title,group,render,options,description]=track,masterPath=approved?resolve(NEON,`${id}.wav`):resolve(OUTPUT,'masters',theme.slug,`${id}.wav`),oggPath=resolve(PUBLIC,theme.slug,`${id}.ogg`)
  const bytes=approved?await readFile(masterPath):wav(master(render(),options)),stats=wavStats(bytes)
  if(!approved)await writeFile(masterPath,bytes)
  const ogg=await encode(masterPath,oggPath,description),decoded=await decodeCheck(oggPath,stats.durationSeconds)
  return {id,title,group,file:`${id}.ogg`,url:`/assets/slots/audio-suites/v1/${theme.slug}/${id}.ogg`,loop:!!options.loop,volume:options.volume,description,...stats,...decoded,bytes:ogg.length,sha256:hash(ogg),source:{type:approved?'approved-original-wav-master':'original-offline-synthesized-composition',path:pathInRepo(masterPath),sha256:hash(bytes),bytes:bytes.length,masterPeakDbfs:stats.peakDbfs,masterRmsDbfs:stats.rmsDbfs},encoding:{codec:'vorbis',container:'ogg',quality:5}}
}
async function main(){
  await mkdir(OUTPUT,{recursive:true});await mkdir(PUBLIC,{recursive:true})
  const generatorHash=hash(await readFile(fileURLToPath(import.meta.url)))
  const all=[],selected=process.argv.slice(2),neonManifest=JSON.parse(await readFile(resolve(NEON,'manifest.json'),'utf8'))
  const themes=[{slug:'neon-nights',title:'Neon Nights',bpm:108,feel:'User-approved original Neon Nights composition; exact existing WAV masters preserved.'},...THEMES]
  for(const theme of themes){
    if(selected.length&&!selected.includes(theme.slug))continue
    cache.clear();seed=parseInt(hash(theme.slug).slice(0,8),16)||1
    await mkdir(resolve(PUBLIC,theme.slug),{recursive:true});await mkdir(resolve(OUTPUT,'masters',theme.slug),{recursive:true})
    const tracks=[]
    const specs=theme.slug==='neon-nights'?neonManifest.tracks.map(t=>[t.id,t.title,t.group,null,{loop:t.loop,volume:t.volume},t.description]):commonTracks(theme)
    if(specs.length!==20||specs.some((s,i)=>s[0]!==IDS[i]))throw Error(`Cue contract mismatch: ${theme.slug}`)
    for(const spec of specs)tracks.push(await save(theme,spec,theme.slug==='neon-nights'))
    const manifest={version:1,slug:theme.slug,title:theme.title,originalComposition:true,theme:theme.feel,bpm:theme.bpm,bonusBpm:theme.slug==='neon-nights'?116:theme.bpm+8,meter:theme.meter||4,palette:theme.slug==='neon-nights'?['approved warm polysynth','electric piano','analog bass','drum machine']:[theme.lead,theme.comp,theme.pad,theme.drum],sampleRate:SR,channels:2,sourceGenerator:pathInRepo(fileURLToPath(import.meta.url)),generatorSha256:generatorHash,provenance:'All sounds are original offline algorithmic compositions and synthesis. No licensed or unlicensed external recordings, borrowed melodies, scraped sound effects, or network services are used. Neon Nights uses the unchanged user-approved original PCM masters.',tracks}
    await writeFile(resolve(PUBLIC,theme.slug,'manifest.json'),JSON.stringify(manifest,null,2)+'\n');all.push(manifest)
    console.log(`${theme.slug}: 20 Ogg cues, ${tracks.filter(t=>t.group==='Music').map(t=>t.durationSeconds.toFixed(2)+'s').join(' / ')}, ${(tracks.reduce((n,t)=>n+t.bytes,0)/1048576).toFixed(2)} MiB; every file decoded and bounded`)
  }
  // Partial rerenders preserve untouched title manifests in the aggregate.
  if(selected.length)for(const theme of themes)if(!selected.includes(theme.slug))all.push(JSON.parse(await readFile(resolve(PUBLIC,theme.slug,'manifest.json'),'utf8')))
  all.sort((a,b)=>themes.findIndex(t=>t.slug===a.slug)-themes.findIndex(t=>t.slug===b.slug))
  const aggregate={version:1,created:'2026-11-02',originalComposition:true,sampleRate:SR,channels:2,codec:'ogg-vorbis',quality:5,cueIds:IDS,suiteCount:all.length,cueCount:all.reduce((n,t)=>n+t.tracks.length,0),totalBytes:all.reduce((n,t)=>n+t.tracks.reduce((m,c)=>m+c.bytes,0),0),suiteManifestPattern:'/assets/slots/audio-suites/v1/{slug}/manifest.json',notes:'Generic cue filenames remain fixed for the shared runtime contract. The four collection cues are musically distinct for each title. Music and reel textures have exact-zero 2ms boundaries, circular mixing and reverb; nonloops have 4ms edge fades. Vorbis encoding and PCM decode verification run for every cue. Runtime synthesis is unnecessary. Excluded existing titles: Midnight Vault (dragon-hoard), Wukong and Pirates.',suites:all}
  await writeFile(resolve(PUBLIC,'manifest.json'),JSON.stringify(aggregate,null,2)+'\n')
  await writeFile(resolve(OUTPUT,'manifest.json'),JSON.stringify(aggregate,null,2)+'\n')
  await writeFile(resolve(OUTPUT,'verification.json'),JSON.stringify({suiteCount:all.length,cueCount:aggregate.cueCount,allDecodeVerified:all.every(t=>t.tracks.every(c=>c.decodeVerified)),maximumDecodedPeakDbfs:Math.max(...all.flatMap(t=>t.tracks.map(c=>c.decodedPeakDbfs))),minimumMusicSeconds:Math.min(...all.flatMap(t=>t.tracks.filter(c=>c.group==='Music').map(c=>c.durationSeconds))),uniqueMusicHashes:new Set(all.flatMap(t=>t.tracks.filter(c=>c.group==='Music').map(c=>c.sha256))).size,totalBytes:aggregate.totalBytes,sourceGeneratorSha256:generatorHash},null,2)+'\n')
  console.log(`Complete: ${aggregate.suiteCount} suites / ${aggregate.cueCount} cues / ${(aggregate.totalBytes/1048576).toFixed(2)} MiB.`)
}
await main()
