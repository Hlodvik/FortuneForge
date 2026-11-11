import * as T from 'three';
import { RoundedBoxGeometry } from 'three/addons/geometries/RoundedBoxGeometry.js';
import { mergeGeometries } from 'three/addons/utils/BufferGeometryUtils.js';

const clip=[new T.Plane(new T.Vector3(0,-1,0),2.52),new T.Plane(new T.Vector3(0,1,0),2.52)];
const material=(color,metalness=.4,roughness=.25,extra={})=>new T.MeshPhysicalMaterial({color,metalness,roughness,clearcoat:1,clearcoatRoughness:.18,clippingPlanes:clip,...extra});
const mats={gold:material('#e8a527',.8,.2),goldLight:material('#ffe4a0',.7,.24),jade:material('#08744a',.22,.2),mint:material('#22bc83',.2,.25),dark:material('#092b22',.3,.28),red:material('#c41b36',.35,.23),orange:material('#ee6722',.45,.27),cream:material('#ffe9c1',.25,.23),pink:material('#ef8cac',.2,.26),pinkDark:material('#a02d66',.25,.3),pearl:material('#ffe6b3',.32,.12,{iridescence:1,iridescenceIOR:1.4,emissive:'#d49d35',emissiveIntensity:.1}),eye:material('#ffdb56',.25,.12,{emissive:'#ff9d00',emissiveIntensity:1.5}),black:material('#091512',.1,.25)};
const sphere=new T.SphereGeometry(1,24,16);
function ellipsoid(parent,mat,p,s){const mesh=new T.Mesh(sphere,mat);mesh.position.set(...p);mesh.scale.set(...s);parent.add(mesh);return mesh;}
function tube(parent,points,radius,mat){const curve=new T.CatmullRomCurve3(points.map(p=>new T.Vector3(...p)));const mesh=new T.Mesh(new T.TubeGeometry(curve,24,radius,8,false),mat);parent.add(mesh);return mesh;}
function torus(parent,radius,thickness,mat,p=[0,0,0]){const m=new T.Mesh(new T.TorusGeometry(radius,thickness,12,64),mat);m.position.set(...p);parent.add(m);return m;}
function cone(parent,radius,height,mat,p,rotation=0){const m=new T.Mesh(new T.ConeGeometry(radius,height,12),mat);m.position.set(...p);m.rotation.z=rotation;parent.add(m);return m;}
// The host symbol mapping supplies one authoritative display label per tile.
function label() {}
function plate(parent,points,depth,mat,z=0,bevel=.025){
  const shape=new T.Shape();points.forEach(([x,y],i)=>i?shape.lineTo(x,y):shape.moveTo(x,y));shape.closePath();
  const mesh=new T.Mesh(new T.ExtrudeGeometry(shape,{depth,bevelEnabled:bevel>0,bevelSegments:2,steps:1,bevelSize:bevel,bevelThickness:bevel,curveSegments:8}),mat);mesh.position.z=z;parent.add(mesh);return mesh;
}
function taperedHorn(parent,points,radius,mat){
  const curve=new T.CatmullRomCurve3(points.map(p=>new T.Vector3(...p))),segments=28,sides=10,frames=curve.computeFrenetFrames(segments,false),vertices=[],uv=[],indices=[];
  for(let i=0;i<=segments;i++){
    const t=i/segments,point=curve.getPointAt(t),r=radius*(1-t)**.8+.001;
    for(let j=0;j<=sides;j++){const angle=j/sides*Math.PI*2;const v=point.clone().addScaledVector(frames.normals[i],Math.cos(angle)*r).addScaledVector(frames.binormals[i],Math.sin(angle)*r);vertices.push(v.x,v.y,v.z);uv.push(j/sides,t);if(i<segments&&j<sides){const n=i*(sides+1)+j;indices.push(n,n+sides+1,n+1,n+1,n+sides+1,n+sides+2);}}
  }
  const geometry=new T.BufferGeometry();geometry.setAttribute('position',new T.Float32BufferAttribute(vertices,3));geometry.setAttribute('uv',new T.Float32BufferAttribute(uv,2));geometry.setIndex(indices);geometry.computeVertexNormals();const mesh=new T.Mesh(geometry,mat);parent.add(mesh);return mesh;
}
function dragon(){
  const g=new T.Group();
  // Broad armored skull, narrow snout and open jaws replace the round animal muzzle.
  plate(g,[[0,.51],[-.29,.40],[-.40,.18],[-.31,-.18],[0,-.31],[.31,-.18],[.40,.18],[.29,.40]],.23,mats.jade,-.12,.045);
  plate(g,[[-.13,.33],[0,.48],[.13,.33],[.09,.02],[0,-.12],[-.09,.02]],.08,mats.mint,.19,.025);
  // Deep red mouth cavity is framed by a distinctly pointed lower jaw.
  plate(g,[[-.23,-.12],[-.30,-.25],[-.23,-.45],[0,-.54],[.23,-.45],[.30,-.25],[.23,-.12]],.06,mats.black,.20,.018);
  plate(g,[[-.17,-.25],[0,-.21],[.17,-.25],[.13,-.43],[0,-.48],[-.13,-.43]],.025,mats.red,.27,.012);
  plate(g,[[-.31,-.26],[-.24,-.47],[0,-.61],[.24,-.47],[.31,-.26],[.16,-.40],[0,-.46],[-.16,-.40]],.13,mats.jade,.14,.025);
  plate(g,[[-.18,-.44],[0,-.52],[.18,-.44],[0,-.62]],.035,mats.gold,.29,.012);
  // Tapered reptile nose projects forward rather than forming a rounded muzzle.
  plate(g,[[-.12,.05],[-.23,-.08],[-.18,-.21],[0,-.24],[.18,-.21],[.23,-.08],[.12,.05]],.20,mats.jade,.28,.035);
  plate(g,[[-.075,.08],[0,.16],[.075,.08],[.055,-.10],[0,-.16],[-.055,-.10]],.045,mats.mint,.49,.014);
  for(const side of [-1,1]){
    // Tall branching horns sweep outward and back. There are no upright oval ears.
    taperedHorn(g,[[side*.27,.36,-.08],[side*.39,.52,-.12],[side*.56,.63,-.20],[side*.62,.79,-.28]],.085,mats.gold);
    taperedHorn(g,[[side*.40,.51,-.12],[side*.50,.48,-.15],[side*.58,.50,-.18]],.044,mats.goldLight);
    for(let i=0;i<4;i++){
      const y=.32-i*.17,outer=.62-i*.02;
      plate(g,[[side*.28,y+.10],[side*outer,y+.17],[side*.48,y-.04],[side*.34,y-.14]],.055,i%2?mats.gold:mats.mint,-.13-i*.005,.012);
    }
    // Slanted ember eyes sit under sharp, heavy brow plates.
    const eye=ellipsoid(g,mats.eye,[side*.215,.145,.34],[.135,.038,.045]);eye.rotation.z=side*.36;
    const pupil=ellipsoid(g,mats.black,[side*.211,.143,.384],[.012,.037,.008]);pupil.rotation.z=side*.1;
    plate(g,[[side*.08,.19],[side*.18,.28],[side*.37,.30],[side*.31,.22],[side*.17,.18]],.055,mats.jade,.35,.012);
    tube(g,[[side*.10,.22,.414],[side*.21,.30,.403],[side*.34,.31,.36]],.018,mats.gold);
    const nostril=ellipsoid(g,mats.black,[side*.13,-.105,.535],[.044,.018,.012]);nostril.rotation.z=side*.25;
    // Prominent upper fangs and smaller lower teeth surround an open mouth.
    cone(g,.043,.23,mats.cream,[side*.213,-.28,.50],Math.PI+side*.12);
    cone(g,.025,.10,mats.cream,[side*.10,-.255,.535],Math.PI);
    cone(g,.023,.09,mats.cream,[side*.13,-.43,.40],side*.12);
    taperedHorn(g,[[side*.17,-.16,.55],[side*.39,-.12,.56],[side*.54,.02,.42],[side*.70,-.03,.31]],.018,mats.goldLight);
    taperedHorn(g,[[side*.25,-.25,.33],[side*.43,-.30,.23],[side*.51,-.22,.08]],.035,mats.mint);
    for(let row=0;row<3;row++)for(let col=0;col<2;col++){
      const scale=new T.Mesh(new T.OctahedronGeometry(.046,0),(row+col)%3?mats.mint:mats.gold);
      scale.position.set(side*(.28+col*.055),.10-row*.105,.20-col*.015);scale.scale.set(1,.65,.30);scale.rotation.z=side*.35;g.add(scale);
    }
  }
  for(let i=0;i<3;i++){const crest=new T.Mesh(new T.OctahedronGeometry(.075-i*.01,0),mats.gold);crest.position.set(0,.41-i*.11,.29+i*.035);crest.scale.set(.65,1.1,.35);g.add(crest);}
  taperedHorn(g,[[0,-.49,.21],[0,-.64,.22],[.055,-.70,.25]],.045,mats.gold);
  label(g,'DRAGON WILD','#ffe08a',-.80);return g;
}
function koi(){const g=new T.Group();
  ellipsoid(g,mats.orange,[0,.04,0],[.30,.5,.24]).rotation.z=-.38;
  ellipsoid(g,mats.cream,[-.14,.26,.17],[.16,.23,.12]).rotation.z=-.5;
  ellipsoid(g,mats.red,[.1,.02,.21],[.14,.15,.04]);
  for(const side of [-1,1]){
    const fin=ellipsoid(g,mats.goldLight,[side*.33,.03,-.02],[.24,.12,.05]);fin.rotation.z=side*.5;
    ellipsoid(g,mats.cream,[side*.13,.34,.2],[.06,.06,.035]);
    ellipsoid(g,mats.black,[side*.13,.35,.23],[.025,.033,.015]);
    const tail=ellipsoid(g,mats.orange,[side*.13,-.48,-.02],[.18,.25,.06]);tail.rotation.z=-side*.7;
  }
  tube(g,[[-.25,.4,.07],[-.43,.45,.12],[-.5,.36,.16]],.013,mats.gold);
  for(let row=0;row<4;row++)for(let col=0;col<3;col++)ellipsoid(g,mats.goldLight,[(col-1)*.12,.15-row*.12,.24],[.04,.027,.012]);
  label(g,'GOLDEN KOI');return g;
}
function lotus(){const g=new T.Group();
  for(let layer=0;layer<3;layer++)for(let i=0;i<7;i++){
    const a=i/7*Math.PI*2+layer*.45;const petal=new T.Group();
    const m=ellipsoid(petal,layer?mats.pink:mats.pinkDark,[0,.27-layer*.025,0],[.13-layer*.01,.34-layer*.035,.10]);m.rotation.x=layer*.15;
    petal.rotation.z=a;petal.position.z=layer*.10;g.add(petal);
  }
  ellipsoid(g,mats.gold,[0,0,.22],[.19,.17,.1]);
  for(let i=0;i<9;i++){const a=i/9*Math.PI*2;ellipsoid(g,mats.goldLight,[Math.cos(a)*.12,Math.sin(a)*.12,.31],[.027,.027,.027]);}
  ellipsoid(g,mats.jade,[0,-.39,-.15],[.5,.13,.07]);label(g,'LOTUS');return g;
}
function jade(){const g=new T.Group();torus(g,.4,.14,mats.jade,[0,.03,0]);torus(g,.405,.025,mats.gold,[0,.03,.145]);
  for(let i=0;i<4;i++){const a=i*Math.PI/2;const m=ellipsoid(g,mats.gold,[Math.cos(a)*.4,.03+Math.sin(a)*.4,.04],[.11,.09,.15]);m.rotation.z=a;}
  tube(g,[[-.08,.48,0],[0,.66,0],[.08,.48,0]],.035,mats.red);
  tube(g,[[0,-.43,0],[.08,-.56,.02],[.02,-.64,.02]],.02,mats.gold);
  for(let i=0;i<5;i++)tube(g,[[.02,-.6,.02],[(i-2)*.045,-.72,.01]],.012,mats.red);
  label(g,'JADE RING','#a0deb9',-.77);return g;
}
function coin(){const g=new T.Group();
  const shape=new T.Shape();shape.absarc(0,0,.47,0,Math.PI*2,false);const hole=new T.Path();hole.moveTo(-.13,-.13);hole.lineTo(-.13,.13);hole.lineTo(.13,.13);hole.lineTo(.13,-.13);hole.closePath();shape.holes.push(hole);
  const geom=new T.ExtrudeGeometry(shape,{depth:.12,bevelEnabled:true,bevelSegments:3,steps:1,bevelSize:.025,bevelThickness:.025,curveSegments:48});
  const m=new T.Mesh(geom,mats.gold);g.add(m);torus(g,.41,.023,mats.goldLight,[0,0,.16]);torus(g,.48,.016,mats.goldLight,[0,0,.12]);
  for(let i=0;i<4;i++){const a=i*Math.PI/2;const stroke=new T.Mesh(new T.BoxGeometry(.18,.05,.03),mats.goldLight);stroke.position.set(Math.cos(a)*.28,Math.sin(a)*.28,.16);stroke.rotation.z=a;g.add(stroke);}
  g.rotation.y=-.15;label(g,'FORTUNE COIN');return g;
}
function bamboo(){const g=new T.Group();
  for(let i=0;i<3;i++){const stalk=new T.Group();stalk.position.x=(i-1)*.24;stalk.rotation.z=(i-1)*-.13;g.add(stalk);
    for(let j=0;j<3;j++){const m=new T.Mesh(new T.CylinderGeometry(.075,.085,.32,12),i===1?mats.mint:mats.jade);m.position.y=j*.33-.30;stalk.add(m);const joint=new T.Mesh(new T.CylinderGeometry(.09,.09,.035,12),mats.goldLight);joint.position.y=j*.33-.47;stalk.add(joint);}
    for(let side of [-1,1]){const leaf=ellipsoid(stalk,mats.jade,[side*.15,.2,.02],[.08,.24,.025]);leaf.rotation.z=-side*.65;}
  }label(g,'BAMBOO');return g;
}
function pearl(){const g=new T.Group();
  torus(g,.48,.035,mats.gold,[0,.03,-.08]);
  ellipsoid(g,mats.pearl,[0,.07,.08],[.37,.37,.37]);
  for(let i=0;i<5;i++){const a=i*Math.PI*.4-Math.PI/2;const leaf=ellipsoid(g,mats.gold,[Math.cos(a)*.33,Math.sin(a)*.33-.08,.1],[.13,.28,.09]);leaf.rotation.z=a-Math.PI/2;}
  ellipsoid(g,mats.cream,[-.1,.2,.39],[.08,.065,.018]);label(g,'SCATTER','#fff1bd');return g;
}
function enrich(g,type){
  const colors={dragon:'#48f6a0',koi:'#f59832',lotus:'#f18bab',jade:'#48cf9b',coin:'#edb644',bamboo:'#65c795',pearl:'#ffe3a2'};
  const canvas=document.createElement('canvas');canvas.width=128;canvas.height=128;const ctx=canvas.getContext('2d');const gradient=ctx.createRadialGradient(64,64,2,64,64,64);gradient.addColorStop(0,colors[type]+'77');gradient.addColorStop(.5,colors[type]+'25');gradient.addColorStop(1,colors[type]+'00');ctx.fillStyle=gradient;ctx.fillRect(0,0,128,128);const texture=new T.CanvasTexture(canvas);texture.colorSpace=T.SRGBColorSpace;
  const halo=new T.Mesh(new T.PlaneGeometry(1.7,1.7),new T.MeshBasicMaterial({map:texture,transparent:true,depthWrite:false,blending:T.AdditiveBlending,clippingPlanes:clip}));halo.position.z=-.23;g.add(halo);
  if(type==='dragon'||type==='pearl'){
    const orbit=torus(g,.62,.012,mats.gold,[0,.03,-.12]);orbit.rotation.x=.15;
    for(let i=0;i<8;i++){const a=i*Math.PI/4;const gem=new T.Mesh(new T.OctahedronGeometry(.025),mats.goldLight);gem.position.set(Math.cos(a)*.62,Math.sin(a)*.62+.03,-.07);g.add(gem);}
  }
  // Merge pieces that share a material, preserving their sculpted geometry.
  // This keeps the animated 3D symbols efficient on mobile GPUs.
  g.updateMatrixWorld(true);const buckets=new Map();g.traverse(child=>{if(!child.isMesh)return;const key=child.material;const bucket=buckets.get(key)||[];let geo=child.geometry.clone().applyMatrix4(child.matrixWorld);if(geo.index)geo=geo.toNonIndexed();bucket.push(geo);buckets.set(key,bucket);});
  const optimized=new T.Group();for(const [mat,geometries]of buckets){const merged=mergeGeometries(geometries,false);if(merged)optimized.add(new T.Mesh(merged,mat));for(const geo of geometries)geo.dispose();}return optimized;
}
const factories={dragon,koi,lotus,jade,coin,bamboo,pearl};
let prototypes;
export function createSymbol(type){prototypes??=Object.fromEntries(Object.entries(factories).map(([key,fn])=>[key,enrich(fn(),key)]));const model=prototypes[type].clone(true);model.userData.type=type;return model;}
export function createFrame(){const g=new T.Group();const gold=new T.MeshStandardMaterial({color:'#c69d51',metalness:.85,roughness:.28});const jade=new T.MeshPhysicalMaterial({color:'#0d3c2d',metalness:.2,roughness:.3,clearcoat:1});
  const box=(w,h,d,x,y,z,mat)=>{const m=new T.Mesh(new RoundedBoxGeometry(w,h,d,3,.06),mat);m.position.set(x,y,z);g.add(m);return m;};
  box(10.25,5.6,.35,0,0,-.7,jade);
  for(const side of [-1,1]){
    box(.13,5.44,.20,side*5.02,0,-.15,gold);box(10.15,.10,.20,0,side*2.68,-.15,gold);
    box(.035,5.15,.08,side*4.85,0,-.08,gold);box(9.7,.035,.08,0,side*2.53,-.08,gold);
    for(const y of [-2.55,2.55]){const gem=new T.Mesh(new T.OctahedronGeometry(.14),gold);gem.position.set(side*4.91,y,.02);g.add(gem);}
  }
  for(let c=0;c<5;c++){
    const x=(c-2)*1.85;box(1.78,5.05,.1,x,0,-.46,new T.MeshBasicMaterial({color:c%2?'#081d18':'#0a241c'}));
    if(c<4)box(.018,4.92,.08,x+.925,0,-.22,gold);
  }
  const lineMat=new T.LineBasicMaterial({color:'#7caa7d',transparent:true,opacity:.13});
  for(const y of [-.83,.83])g.add(new T.Line(new T.BufferGeometry().setFromPoints([new T.Vector3(-4.65,y,-.27),new T.Vector3(4.65,y,-.27)]),lineMat));
  return g;
}
