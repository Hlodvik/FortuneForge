export class GameAudio {
  enabled=false; context=null;
  async toggle(){this.enabled=!this.enabled;if(this.enabled){this.context??=new AudioContext();await this.context.resume();this.note(523,.12,.06);}return this.enabled;}
  note(frequency,duration=.15,volume=.035,delay=0,type='sine'){
    if(!this.enabled||!this.context)return;
    const ctx=this.context,t=ctx.currentTime+delay,osc=ctx.createOscillator(),gain=ctx.createGain();
    osc.type=type;osc.frequency.setValueAtTime(frequency,t);gain.gain.setValueAtTime(0,t);gain.gain.linearRampToValueAtTime(volume,t+.008);gain.gain.exponentialRampToValueAtTime(.0001,t+duration);
    osc.connect(gain);gain.connect(ctx.destination);osc.start(t);osc.stop(t+duration+.02);
  }
  spin(){for(let i=0;i<7;i++)this.note(130+i*15,.08,.012,i*.08,'triangle');}
  land(index){this.note(260+index*65,.12,.035,0,'triangle');this.note(1040+index*130,.08,.009);}
  win(big=false){const notes=big?[523,659,784,1047,1319]:[523,659,784];notes.forEach((f,i)=>{this.note(f,.6,.055,i*.11);this.note(f*2,.3,.008,i*.11);});}
  bonus(){[392,523,659,784,1047,1319].forEach((f,i)=>this.note(f,1,.05,i*.15));}
  feature(mode){
    const notes=mode==='garden'?[392,523,659,784]:mode==='moon'?[523,784,1047,1568]:[147,196,294,587,1175];
    notes.forEach((f,i)=>this.note(f,mode==='moon'?.7:.4,.04,i*.09,mode==='wrath'?'triangle':'sine'));
  }
}
