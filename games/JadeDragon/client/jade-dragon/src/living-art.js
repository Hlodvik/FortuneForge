// Layered raster animation: preserves generated artwork, adds live light and particles.
// This does not create or impersonate a freely rotating 3D character mesh.
const images=new Map();
export const ART_BY_BONUS={garden:'bonus',moon:'pearl',wrath:'dragon'};
export function loadArtwork(kind){
 if(!images.has(kind))images.set(kind,new Promise((resolve,reject)=>{const image=new Image();image.onload=()=>resolve(image);image.onerror=()=>reject(Error('Artwork could not load: '+kind));image.src=`${import.meta.env.BASE_URL}textures/living-art/${kind}.webp`;}));
 return images.get(kind);
}
export class LivingArtwork{
 constructor(host){
  this.host=host;this.canvas=document.createElement('canvas');this.canvas.setAttribute('aria-hidden','true');host.append(this.canvas);this.ctx=this.canvas.getContext('2d');this.active=false;this.playing=true;this.reduced=matchMedia('(prefers-reduced-motion: reduce)').matches;this.time=0;this.pointer={x:0,y:0};this.serial=0;
  this.resizeObserver=new ResizeObserver(()=>this.resize());this.resizeObserver.observe(host);
  host.addEventListener('pointermove',event=>{const r=host.getBoundingClientRect();this.pointer={x:(event.clientX-r.left)/r.width-.5,y:(event.clientY-r.top)/r.height-.5};});host.addEventListener('pointerleave',()=>{this.pointer={x:0,y:0};});
  document.addEventListener('visibilitychange',()=>{cancelAnimationFrame(this.raf);if(this.active&&!document.hidden)this.animate();});
 }
 static preload(){return Promise.all(['dragon','pearl','bonus'].map(loadArtwork));}
 resize(){const dpr=Math.min(devicePixelRatio,1.75);this.width=this.host.clientWidth;this.height=this.host.clientHeight;this.canvas.width=Math.max(1,Math.round(this.width*dpr));this.canvas.height=Math.max(1,Math.round(this.height*dpr));this.ctx.setTransform(dpr,0,0,dpr,0,0);if(this.active&&this.image)this.draw();}
 async start(kind){const serial=++this.serial;cancelAnimationFrame(this.raf);this.kind=kind;this.active=true;this.playing=true;this.image=null;this.time=0;this.intensity=0;this.host.dataset.art=kind;this.host.dataset.state='loading';
  try{const image=await loadArtwork(kind);if(serial!==this.serial)return;this.image=image;this.host.dataset.state='ready';this.resize();this.animate();}
  catch(error){if(serial===this.serial){this.active=false;this.host.dataset.state='error';}throw error;}
 }
 stop(){this.serial++;this.active=false;cancelAnimationFrame(this.raf);this.ctx.clearRect(0,0,this.width||1,this.height||1);}
 toggle(){this.playing=!this.playing;return this.playing;}
 burst(){this.intensity=1;if(this.reduced)this.draw();}
 animate(){cancelAnimationFrame(this.raf);let last=performance.now();const frame=now=>{if(!this.active||document.hidden)return;const dt=Math.min((now-last)/1000,.05);last=now;if(this.playing&&!this.reduced){this.time+=dt;this.intensity=Math.max(0,this.intensity-dt*.55);}this.draw();if(!this.reduced)this.raf=requestAnimationFrame(frame);};frame(last);}
 draw() {
  paintArtwork(this.ctx, {
   image: this.image, width: this.width, height: this.height, kind: this.kind,
   time: this.time, intensity: this.intensity, pointer: this.pointer, reduced: this.reduced,
  });
 }
}

export function paintArtwork(context, options) {
 new ArtworkFrame(context, options).draw();
}

class ArtworkFrame {
 constructor(context, { image, width, height, kind, time = 0, intensity = 0, pointer = { x: 0, y: 0 }, reduced = false }) {
  this.ctx = context;
  this.image = image;
  this.width = width;
  this.height = height;
  this.kind = kind;
  this.time = reduced ? 0 : time;
  this.intensity = intensity;
  this.pointer = pointer;
  this.reduced = reduced;
 }

 light(x,y,r,color,alpha=1){const c=this.ctx,g=c.createRadialGradient(x,y,0,x,y,r);g.addColorStop(0,`rgba(${color},${alpha})`);g.addColorStop(.25,`rgba(${color},${alpha*.45})`);g.addColorStop(1,`rgba(${color},0)`);c.fillStyle=g;c.fillRect(x-r,y-r,r*2,r*2);}
 draw(){
  if(!this.image||!this.width||!this.height)return;const c=this.ctx,w=this.width,h=this.height,t=this.time,kind=this.kind;
  c.clearRect(0,0,w,h);const s=Math.min(w*.91,h*.96),cx=w/2,cy=h*.48,p=this.reduced?{x:0,y:0}:this.pointer;
  c.save();c.translate(cx+p.x*s*.025,cy+p.y*s*.012);c.rotate(kind==='dragon'?Math.sin(t*.7)*.012:0);
  const breathe=this.reduced?1:1+Math.sin(t*1.8)*.008;c.scale(breathe,breathe);
  c.globalCompositeOperation='lighter';this.light(0,0,s*.62,kind==='dragon'?'255,131,24':kind==='pearl'?'166,74,255':'40,255,164',.13+this.intensity*.15);c.globalCompositeOperation='source-over';
  if(kind==='dragon'){
   // Small row displacements give the mane a gentle sway without rotating the head.
   const strips=40;for(let i=0;i<strips;i++){const v=i/strips,shift=this.reduced?0:Math.sin(t*1.7+v*5)*s*.0025*Math.max(0,v-.3);c.drawImage(this.image,0,i*this.image.height/strips,this.image.width,this.image.height/strips+1,-s/2+shift,-s/2+i*s/strips,s,s/strips+1);}
  }else c.drawImage(this.image,-s/2,-s/2,s,s);
  c.globalCompositeOperation='lighter';
  if(kind==='dragon')this.dragon(t,s);else if(kind==='pearl')this.pearl(t,s);else this.portal(t,s);
  this.particles(t,s);c.restore();
 }
 dragon(t,s){const c=this.ctx,pulse=.25+(Math.sin(t*2.1)+1)*.12+this.intensity*.3;
  this.light(s*.008,-s*.12,s*.035,'255,173,27',pulse);this.light(s*.21,-s*.12,s*.03,'255,173,27',pulse);
  this.light(s*.105,s*.12,s*.10,'255,96,10',.15+this.intensity*.3);
  for(let j=0;j<3;j++){const phase=(t*.6+j*.31)%1,x=s*.105,y=s*.12,length=s*(.08+phase*.13)*(1+this.intensity*.5),bend=Math.sin(t*3+j)*s*.028;
   c.beginPath();c.moveTo(x-s*.024,y);c.bezierCurveTo(x-s*.035+bend,y+length*.35,x+bend,y+length*.8,x+bend*.7,y+length);c.bezierCurveTo(x+s*.025,y+length*.55,x+s*.027,y+length*.18,x+s*.024,y);c.closePath();const gradient=c.createLinearGradient(x,y,x,y+length);gradient.addColorStop(0,'rgba(255,200,56,.10)');gradient.addColorStop(.45,`rgba(255,116,16,${.07+this.intensity*.12})`);gradient.addColorStop(1,'rgba(255,50,8,0)');c.fillStyle=gradient;c.fill();}
 }
 pearl(t,s){const c=this.ctx;this.light(0,-s*.11,s*.26,'180,93,255',.09+(Math.sin(t*2)+1)*.035+this.intensity*.18);
  // Passing light is clipped to the pearl silhouette, leaving its painted detail visible.
  c.save();c.beginPath();c.arc(0,-s*.12,s*.285,0,Math.PI*2);c.clip();const sweep=Math.sin(t*.75)*s*.43,g=c.createLinearGradient(sweep-s*.14,-s*.4,sweep+s*.14,s*.1);g.addColorStop(0,'rgba(255,235,255,0)');g.addColorStop(.5,'rgba(255,235,255,.14)');g.addColorStop(1,'rgba(255,235,255,0)');c.fillStyle=g;c.fillRect(-s*.4,-s*.5,s*.8,s*.8);c.restore();
  for(let j=0;j<3;j++){const a=t*(.7+j*.13)+j*2.1,x=Math.cos(a)*s*.35,y=-s*.11+Math.sin(a)*s*.14;this.light(x,y,s*.025,'236,185,255',.8);}
 }
 portal(t,s){const c=this.ctx;
  // Animate only the energy inside the portal; keep the ornate frame stable.
  c.save();c.beginPath();c.arc(0,0,s*.285,0,Math.PI*2);c.clip();c.rotate(this.reduced?0:t*.24);c.globalCompositeOperation='source-over';c.drawImage(this.image,-s/2,-s/2,s,s);c.restore();c.globalCompositeOperation='lighter';
  this.light(0,0,s*.22,'78,255,155',.12+(Math.sin(t*2)+1)*.035+this.intensity*.22);
  for(let j=0;j<3;j++){const r=s*(.25+((t*.09+j*.12)%.22));c.strokeStyle=`rgba(111,255,183,${.13*(1-(r/s-.25)/.22)})`;c.lineWidth=1.1;c.beginPath();c.ellipse(0,0,r,r,0,0,Math.PI*2);c.stroke();}
 }
 particles(t,s){const c=this.ctx,kind=this.kind,count=this.reduced?10:40;
  for(let i=0;i<count;i++){const seed=i*2.399,life=(t*(kind==='dragon'?.24:.1)+i*.137)%1;let x,y,alpha=.2+Math.sin(life*Math.PI)*.65;
   if(kind==='dragon'){x=s*.105+Math.sin(seed)*s*life*.40;y=s*.12-life*s*.70;}else{const radius=s*(.25+(i%7)*.036+this.intensity*.08),a=seed+t*(kind==='bonus'?.33:.17);x=Math.cos(a)*radius;y=Math.sin(a)*radius*(kind==='pearl'?.75:1)-(kind==='pearl'?s*.08:0);alpha*=.6+(Math.sin(t*3+i)+1)*.2;}
   const r=s*(.0017+(i%3)*.0009)*(1+this.intensity*.7);
   if(kind==='bonus'&&i%3===0){c.save();c.translate(x,y);c.rotate(seed+t*.8);c.globalCompositeOperation='source-over';c.fillStyle=`rgba(85,255,155,${alpha*.8})`;c.beginPath();c.moveTo(0,-r*4);c.quadraticCurveTo(r*3,0,0,r*4);c.quadraticCurveTo(-r*2,0,0,-r*4);c.fill();c.restore();}
   else{this.light(x,y,r*3.5,kind==='pearl'?'203,135,255':kind==='dragon'?'255,166,45':'255,226,121',alpha*.65);c.fillStyle=`rgba(255,236,180,${alpha})`;c.fillRect(x-r*.4,y-r*2,r*.8,r*4);c.fillRect(x-r*2,y-r*.4,r*4,r*.8);}
  }
 }
}
