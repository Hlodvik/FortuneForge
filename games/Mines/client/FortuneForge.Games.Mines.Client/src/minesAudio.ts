import chip from './assets/chip-lay-1.ogg'

let context:AudioContext|null=null
export function unlockMinesAudio(){
  try{context??=new AudioContext();void context.resume().catch(()=>undefined)}catch{/* Browser audio is optional. */}
}
export function playMinesAudio(cue:'wager'|'gem'|'mine'|'cashout',muted:boolean){
  if(muted)return
  if(cue==='wager'){
    try{const audio=new Audio(chip);audio.volume=.3;void audio.play()?.catch(()=>undefined)}catch{/* Browser audio is optional. */}return
  }
  try{
    if(!context||context.state!=='running')return
    const now=context.currentTime
    const frequencies=cue==='gem'?[660,990,1320]:cue==='cashout'?[523.25,659.25,783.99,1046.5]:[100,62,38]
    frequencies.forEach((frequency,i)=>{
      const osc=context!.createOscillator(),gain=context!.createGain(),start=now+i*.055
      osc.type=cue==='mine'?'triangle':'sine';osc.frequency.setValueAtTime(frequency,start)
      if(cue==='mine')osc.frequency.exponentialRampToValueAtTime(frequency*.5,start+.25)
      gain.gain.setValueAtTime(0,start);gain.gain.linearRampToValueAtTime(cue==='mine'?.15:.07,start+.01);gain.gain.exponentialRampToValueAtTime(.001,start+.3)
      osc.connect(gain);gain.connect(context!.destination);osc.start(start);osc.stop(start+.32)
    })
  }catch{/* Never let audio interrupt settlement. */}
}
