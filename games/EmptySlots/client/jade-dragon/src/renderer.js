import * as T from 'three';
import {RoomEnvironment} from 'three/addons/environments/RoomEnvironment.js';
import {createSymbol} from './models.js';
import {SYMBOLS,validateGrid} from './symbols.js';
export class JadeRenderer{
 constructor(host){this.host=host;this.motion=!matchMedia('(prefers-reduced-motion:reduce)').matches;this.renderer=new T.WebGLRenderer({antialias:true,alpha:true});this.renderer.setPixelRatio(Math.min(devicePixelRatio,1.5));this.renderer.toneMapping=T.ACESFilmicToneMapping;this.renderer.toneMappingExposure=.92;host.append(this.renderer.domElement);this.scene=new T.Scene();this.camera=new T.OrthographicCamera(-5.2,5.2,3.5,-3.5,.1,50);this.camera.position.z=20;const env=new T.PMREMGenerator(this.renderer),room=new RoomEnvironment();this.environment=env.fromScene(room,.04);room.dispose();env.dispose();this.scene.environment=this.environment.texture;this.scene.environmentIntensity=.5;this.scene.add(new T.AmbientLight('#e8ffe3',.6));const light=new T.DirectionalLight('#ffe7ab',2.5);light.position.set(-3,5,8);this.scene.add(light);this.tiles=new T.Group();this.scene.add(this.tiles);this.objects=[];this.time=0;this.labels=new Map();this.art=new Map();for(const [id,file] of [['S07','dragon'],['S08','pearl'],['S20','bonus'],['S21','pearl'],['S22','dragon']]){const texture=new T.TextureLoader().load(`/games/jade-dragon/textures/living-art/${file}.webp`);texture.colorSpace=T.SRGBColorSpace;this.art.set(id,texture);}this.observer=new ResizeObserver(()=>this.resize());this.observer.observe(host);this.resize();this.renderer.setAnimationLoop(now=>this.frame(now));this.renderer.domElement.addEventListener('webglcontextlost',event=>{event.preventDefault();this.host.dispatchEvent(new Event('renderer-lost'));});}
 resize(){
  const w=this.host.clientWidth,h=this.host.clientHeight;if(!w||!h)return;
  const aspect=w/h,width=Math.max(10.4,7*aspect),height=Math.max(7,10.4/aspect);
  this.camera.left=-width/2;this.camera.right=width/2;this.camera.top=height/2;this.camera.bottom=-height/2;
  this.camera.updateProjectionMatrix();this.renderer.setSize(w,h);this.layout();
 }
 layout(){
  const step=(this.camera.right-this.camera.left-.75)/5;
  for(const cell of this.objects){cell.group.position.x=(cell.col-2)*step;cell.bg.scale.x=(step-.05)/1.88;}
 }
 refreshLabels(){
  const previous=[...this.labels.values()];this.labels.clear();
  if(this.grid)for(const cell of this.objects)cell.label.material.map=this.label(this.grid[cell.col][cell.row]);
  for(const texture of previous)texture.dispose();
 }
 label(id){
  if(this.labels.has(id))return this.labels.get(id);
  const canvas=document.createElement('canvas');canvas.width=300;canvas.height=48;
  const c=canvas.getContext('2d'),name=SYMBOLS[id][1].toUpperCase();
  const font=size=>`700 ${size}px "Cormorant Garamond", Georgia, serif`;
  c.font=font(52);let metrics=c.measureText(name);
  const glyphHeight=metrics.actualBoundingBoxAscent+metrics.actualBoundingBoxDescent;
  if(glyphHeight>0){c.font=font(52*38/glyphHeight);metrics=c.measureText(name);}
  const baseline=(canvas.height+metrics.actualBoundingBoxAscent-metrics.actualBoundingBoxDescent)/2;
  c.textAlign='center';c.fillStyle='#fff0bd';c.strokeStyle='#031b12';c.lineWidth=2;
  c.strokeText(name,150,baseline,284);c.fillText(name,150,baseline,284);
  const texture=new T.CanvasTexture(canvas);this.labels.set(id,texture);return texture;
 }
 setGrid(grid){validateGrid(grid);for(const cell of this.objects){cell.label.material.dispose();cell.bg.material.dispose();cell.bg.geometry.dispose();if(cell.art)cell.art.material.dispose();}this.tiles.clear();this.objects=[];this.grid=grid;const rows=grid[0].length,step=6.35/rows;
  for(let col=0;col<5;col++)for(let row=0;row<rows;row++){const id=grid[col][row],group=new T.Group();group.position.set((col-2)*1.93,(rows/2-.5-row)*step,0);const bg=new T.Mesh(new T.PlaneGeometry(1.88,step-.05),new T.MeshBasicMaterial({color:(col+row)%2?'#0a3024':'#102b21',transparent:true,opacity:.85}));bg.position.z=-.6;group.add(bg);let art=null,model=null;
   if(this.art.has(id)){art=new T.Sprite(new T.SpriteMaterial({map:this.art.get(id),transparent:true,depthWrite:false}));art.scale.set(step*.91,step*.91,1);art.position.y=.07;group.add(art);}else{model=createSymbol(SYMBOLS[id][0]);model.scale.setScalar(Math.min(.70,step*.43));model.position.y=.10;if(id==='S06')model.rotation.z=.20;if(id==='S09')model.rotation.y=.45;group.add(model);}
   const label=new T.Sprite(new T.SpriteMaterial({map:this.label(id),transparent:true,depthWrite:false}));label.scale.set(1.7,.27,1);label.position.set(0,-step*.40,.9);group.add(label);this.tiles.add(group);this.objects.push({group,model,label,bg,art,col,row,y:group.position.y});}
  this.layout();
  if(!this.fontRequested){this.fontRequested=true;document.fonts.load('700 52px "Cormorant Garamond"').then(()=>this.refreshLabels()).catch(()=>{});}
 }
 async spin(grid,{instant=false}={}){this.setGrid(grid);this.wins=new Set();if(!this.motion||instant){this.renderer.render(this.scene,this.camera);return;}const start=performance.now();await new Promise(resolve=>{this.animation={last:start,elapsed:0,resolve};});}
 finishSpin(){if(!this.animation)return;const {resolve}=this.animation;this.animation=null;for(const cell of this.objects){cell.group.position.y=cell.y;cell.group.scale.y=1;}this.renderer.render(this.scene,this.camera);resolve();}
 celebrate(result){this.wins=new Set(result.payout.paylines.flatMap(line=>line.matches.flatMap(p=>p.match.positions.map(({reel,row})=>`${reel}:${row}`))));}
 frame(now){const t=now/1000;if(this.animation){this.animation.elapsed+=Math.max(0,now-this.animation.last)*(this.speed||1);this.animation.last=now;}for(const cell of this.objects){if(this.animation){const elapsed=this.animation.elapsed/1000,stop=1.8+cell.col*.28,p=Math.min(1,elapsed/stop);cell.group.position.y=cell.y+(1-p)*Math.sin(elapsed*9+cell.row)*.45;cell.group.scale.y=.85+.15*p;}else{cell.group.position.y=cell.y;cell.group.scale.y=1;if(cell.model)cell.model.rotation.y=this.motion?Math.sin(t*.7+cell.col)*.12:0;cell.bg.material.color.set(this.wins?.has(`${cell.col}:${cell.row}`)?'#466d25':(cell.col+cell.row)%2?'#0a3024':'#102b21');}}
  if(this.animation&&this.animation.elapsed>=3200)this.finishSpin();this.renderer.render(this.scene,this.camera);}
}
