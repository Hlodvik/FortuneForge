import './style.css';
import './fortune.css';
import './laptop.css';
import './fit-viewport.js';
import {callHost} from './bridge.js';
import {JadeRenderer} from './renderer.js';
import {LivingArtwork} from './living-art.js';
import {GameAudio} from './audio.js';
import {BET_POINTS,CASCADE_SYMBOL_IDS,SYMBOLS,validateGrid} from './symbols.js';
const $ = selector => document.querySelector(selector);
const zar = new Intl.NumberFormat('en-ZA', { style: 'currency', currency: 'ZAR' });
const money = cents => zar.format(cents / 100);
const audio=new GameAudio(),reduced=matchMedia('(prefers-reduced-motion:reduce)').matches;
const wait=ms=>new Promise(resolve=>setTimeout(resolve,reduced?80:ms));
const number=new Intl.NumberFormat('en-ZA');
let state=null,rules=null,balance=0,bet=20,busy=false,auto=false,ready=false,uncertain=false,win=0,timer,featureTotal=0,modal=false,instantRequested=false,skipCountdown=null;
let cascadeMultiplier = null;
let roundIsFree = false;
let pointValueInCents = 25;
const maximumBet = () => 'Maximum bet ' + money(BET_POINTS.at(-1) * pointValueInCents);
function finishCurrentSpin(){
 if(!busy)return;
 instantRequested=true;
 skipCountdown?.();
 renderer?.finishSpin();
 renderer?.finishCascade();
}
$('#instant-results').addEventListener('change',()=>{if(renderer)renderer.speed=$('#instant-results').checked?2:1;});
function countdown(){return new Promise(resolve=>{const done=()=>{clearTimeout(timeout);skipCountdown=null;resolve();};const timeout=setTimeout(done,reduced?80:450/(renderer?.speed||1));skipCountdown=done;});}
const artHost=document.createElement('div');artHost.className='fortune-arrival';$('#bonus-arrival').prepend(artHost);const art=new LivingArtwork(artHost);
let renderer;try{renderer=new JadeRenderer($('#canvas-host'));renderer.setGrid([['S01','S02','S03','S04'],['S04','S05','S06','S01'],['S07','S08','S01','S02'],['S05','S02','S04','S03'],['S08','S01','S03','S04']]);}catch{$('#message').textContent='3D could not start. Enable browser hardware acceleration and reload.';}
function update(){
 const free=state?.freeSpinsRemaining>0,points=free?state.freeSpinWagerPoints:bet;
 const cascade=rules?.cascadeRules;
 const inFreeFeature=free||(Boolean(cascade)&&busy&&roundIsFree);
 $('#balance').textContent=money(balance);$('#bet').textContent=money(points*pointValueInCents);$('#last-win').textContent=money(win);$('#round-total').textContent=money(inFreeFeature?featureTotal:win);
 $('#spin').disabled=!ready||modal||auto||uncertain;$('#spin-label').textContent=busy?'REVEAL':free?'FREE SPIN':'SPIN';$('#spin').setAttribute('aria-label',busy?'Show current spin result instantly':'Spin reels');$('#spin').setAttribute('aria-busy',String(busy));
 for(const id of ['#bet-minus','#bet-plus'])$(id).disabled=!ready||busy||free||auto||uncertain;
 $('#bet-minus').disabled||=bet===BET_POINTS[0];$('#bet-plus').disabled||=bet===BET_POINTS.at(-1);
 $('#auto').disabled=!ready||uncertain;$('#auto-label').textContent=auto?'STOP AUTO':'AUTO';$('#auto').setAttribute('aria-pressed',String(auto));$('#auto').classList.toggle('active',auto);
 $('#jade-game').classList.toggle('bonus-active',inFreeFeature);$('#jade-game').dataset.bonus=state?.freeSpinFeatureMode?'wrath':inFreeFeature?'moon':'';
 $('#feature-name').textContent=state?.freeSpinFeatureMode?'DRAGON’S AWAKENING':inFreeFeature?'MOONLIT FREE GAMES':cascade?'JADE CASCADES':'JADE DRAGON';
 $('#feature-caption').textContent=free?`${state.freeSpinsRemaining} free games remaining · wager locked`:cascade?'Winning symbols clear and new symbols fall':'23 paylines · Server-settled outcomes';
 $('#feature-meter-label').textContent=inFreeFeature?'SESSION FEATURE WIN':'ROUND WIN';$('#free-spins').textContent=free?`${state.freeSpinsRemaining} free spins remaining`:maximumBet();
 $('#free-games-badge').hidden=!free;$('#free-games-badge-count').textContent=`${state?.freeSpinsRemaining??0} LEFT`;
 $('#boost-row').hidden=Boolean(cascade);
 $('#boost').disabled=Boolean(cascade)||!ready||busy||free||auto||uncertain||(state?.specialPointsBalance??0)<8;$('#special-count').textContent=state?.specialPointsBalance??0;
 if(free)showFree();else $('#free-games-popup').hidden=true;
 if(cascade){
  const multipliers=(busy?roundIsFree:free)?cascade.freeSpinMultipliers:cascade.baseMultipliers;
  $('#multiplier').textContent=cascadeMultiplier===null?'CASCADES':cascadeMultiplier+'× CASCADE';
  renderMultiplierLadder(multipliers);
  return;
 }
 const collection=state?.sealCollections??[];$('#multiplier-ladder').innerHTML=['track1','track2','track3','track4'].map((id,i)=>{const p=collection.find(x=>x.sealId===id);return `<span class="fortune-seal"><b>${['GARDEN','MOON','DRAGON','GOLD'][i]}</b>${p?.count??0}/${p?.requiredCount??22}</span>`;}).join('');
}
function renderMultiplierLadder(multipliers) {
 const ladder = $('#multiplier-ladder');
 ladder.setAttribute('aria-label', 'Cascade multipliers');
 ladder.replaceChildren(...multipliers.map(multiplier => {
  const node = document.createElement('span');
  node.className = 'fortune-seal cascade-multiplier';
  node.textContent = multiplier + '×';
  node.classList.toggle('active', multiplier === cascadeMultiplier);
  return node;
 }));
}
function showFree(){if(!state?.freeSpinsRemaining)return;$('#free-popup-count').textContent=state.freeSpinsRemaining;$('#free-popup-title').textContent='FREE GAMES';$('#free-popup-caption').textContent='REMAINING';$('#free-games-popup').hidden=busy;}
function stop(){auto=false;clearTimeout(timer);update();}
async function init(){
 try{
  const data=await callHost('init');
  state=data.state;balance=data.balanceCents;rules=data.rules;
  pointValueInCents=rules.wagering?.pointValueInCents??pointValueInCents;
  $('#maximum-bet').textContent=maximumBet();
  if(rules.cascadeRules&&renderer&&!state.freeSpinsRemaining){
   const ids=CASCADE_SYMBOL_IDS;
   renderer.setGrid(Array.from({length:5},(_,reel)=>Array.from({length:4},(_,row)=>ids[(reel*3+row)%ids.length])));
  }
  renderRules();ready=Boolean(renderer);$('#loading').hidden=true;
  $('#message').textContent='Your Fortune Forge balance is connected. Spin when ready.';
  update();
 }catch(error){$('#loading p').textContent=error.message;$('#message').textContent=error.message;}
}
async function arrival(mode,count){const overlay=$('#bonus-arrival');overlay.dataset.theme=mode==='dragon'?'wrath':mode==='pearl'?'moon':'garden';$('#arrival-name').textContent=mode==='dragon'?'Dragon’s Awakening':'Free Games';$('#arrival-spins').textContent=`${count} FREE GAMES`;overlay.hidden=false;$('#free-games-popup').hidden=true;audio.feature(mode==='dragon'?'wrath':'moon');try{await art.start(mode);await wait(1400);}catch{await wait(300);}finally{art.stop();overlay.hidden=true;}}
async function playCascade(result, previousFeatureTotal, payoutPointValueInCents) {
 const { steps } = result.jadeDragonCascade;
 const toast = $('#special-toast');
 const finalGrid = steps.at(-1)?.reels ?? result.reels;
 let displayedCents = 0;
 try {
  for (const [index, step] of steps.entries()) {
   if (instantRequested || reduced || !ready) break;
   cascadeMultiplier = step.multiplier;
   const awardCents = step.amountPoints * payoutPointValueInCents;
   displayedCents += awardCents;
   win = displayedCents;
   if (roundIsFree) featureTotal = previousFeatureTotal + win;
   toast.textContent = 'Cascade ' + (index + 1) + ' · ' + step.multiplier + '× · +' + money(awardCents);
   toast.hidden = false;
   update();
   await renderer.playCascadeStep(step);
  }
 } finally {
  toast.hidden = true;
  cascadeMultiplier = null;
 }
 renderer.setGrid(finalGrid);
 return finalGrid;
}

async function spin(boost=false){
 if(!ready||busy||modal||uncertain||(boost&&rules.cascadeRules))return;
 const free=state.freeSpinsRemaining>0,wager=free?state.freeSpinWagerPoints:bet;
 if(!free&&balance<wager*pointValueInCents){stop();$('#message').textContent='Your balance is too low for this bet.';return;}
 const previousFeatureTotal=free?featureTotal:0;
 roundIsFree=free;busy=true;instantRequested=false;renderer.speed=$('#instant-results').checked?2:1;
 win=0;$('#free-games-popup').hidden=true;update();
 try{
  if(free&&!instantRequested){$('#free-popup-count').textContent=state.freeSpinsRemaining;$('#free-games-popup').hidden=false;await countdown();$('#free-games-popup').hidden=true;}
  if(!ready||uncertain)return;
  if(!instantRequested)renderer.startSpin();
  audio.spin();
  const request={wagerPoints:wager,useFreeSpin:free,useSpecialBoost:boost};
  if(rules.cascadeRules)request.jadeDragonEngineVersion=rules.engineVersion;
  const result=await callHost('spin',request);
  validateGrid(result.reels);
  state=result;balance=result.balanceCents;
  const payoutPointValueInCents=result.payoutPointValueInCents??result.pointValueInCents;
  const settledWin=result.payout.totalPoints*payoutPointValueInCents;
  if(!result.jadeDragonCascade){win=settledWin;featureTotal=previousFeatureTotal+(free?win:0);}
  await renderer.settleSpin(result.reels,{instant:instantRequested,onLand:reel=>audio.land(reel)});
  if(!ready)return;

  let finalGrid=result.reels;
  if(result.jadeDragonCascade){
   finalGrid=await playCascade(result,previousFeatureTotal,payoutPointValueInCents);
   win=settledWin;
   featureTotal=previousFeatureTotal+(free?win:0);
  }else{
   renderer.celebrate(result);
  }
  if(!ready)return;
  if(win>0)audio.win(win>=wager*result.pointValueInCents*20);
  $('#grid-accessible').textContent=finalGrid.map((reel,index)=>'Reel '+(index+1)+': '+reel.map(id=>SYMBOLS[id][1]).join(', ')).join('. ');
  $('#message').textContent=win>0?money(win)+' won · confirmed by Fortune Forge':'Round complete. Your next spin is up to you.';
  if(!instantRequested&&result.freeSpinsRemaining>0&&(!free||result.freeSpinsAwarded>0)){
   await arrival(result.freeSpinFeatureMode?'dragon':'pearl',result.freeSpinsRemaining);
  }
 }catch(error){
  renderer.finishSpin();renderer.finishCascade();stop();uncertain=true;
  $('#message').textContent=error.message+' Reload to recover the server balance; this request will not be repeated automatically.';
 }finally{
  busy=false;update();
  if(auto&&!document.hidden&&!uncertain)timer=setTimeout(()=>spin(),win?900:500);
 }
}

function paytableRow(id, payouts) {
 const row = document.createElement('div');
 row.className = 'paytable-row';
 for (const text of [SYMBOLS[id]?.[1] ?? id, ...payouts]) {
  const node = document.createElement('span');
  node.textContent = text;
  row.append(node);
 }
 return row;
}

function renderRules() {
 const cascade = rules.cascadeRules;
 const paytable = $('#paytable');
 if (!cascade) {
  paytable.replaceChildren(...rules.symbols.filter(symbol => symbol.paylinePayoutRules.length).map(symbol =>
   paytableRow(symbol.symbolId, [3, 4, 5].map(length =>
    symbol.paylinePayoutRules.find(payout => payout.matchLength === length)?.multiplier ?? '—',
   )),
  ));
  return;
 }

 const ways = number.format(cascade.ways);
 $('#ways-label').querySelector('strong').textContent = ways;
 $('#ways-label').lastChild.textContent = ' WAYS';
 $('#pearl-promise').textContent = cascade.requiredPearls + ' PEARLS UNLOCK FREE SPINS';
 $('#rules-summary').textContent = 'Match symbols on consecutive reels starting from the left. Each matching combination across those reels pays, with up to ' + ways + ' ways on the 5×4 grid.';
 $('#legacy-rules-details').hidden = true;
 const details = $('#cascade-rules-details');
 details.hidden = false;
 const paragraphs = [
  'Winning symbols clear and new symbols fall into place. Base cascade multipliers are ' + cascade.baseMultipliers.map(value => value + '×').join(' / ') +
   ', and free-spin multipliers are ' + cascade.freeSpinMultipliers.map(value => value + '×').join(' / ') + '. The highest multiplier continues for later cascades in that spin.',
  cascade.requiredPearls + ' Golden Pearls on the final board award ' + cascade.baseFreeSpins + ' free spins. During free spins, they retrigger ' +
   cascade.retriggerFreeSpins + ' spins. Each additional pearl adds ' + cascade.spinsPerExtraPearl + ' spins. Free-spin wagers stay locked.',
  'A spin can pay up to ' + number.format(cascade.maximumWinMultiplier) + '× the wager across ' + cascade.maximumWinningSteps + ' winning stages. The server calculates and settles the total.',
  'The table shows the wager multiplier per winning way. Wagers use ' + money(pointValueInCents) + ' points. Awards are paid to the cent. ' + maximumBet() + '.',
 ];
 details.replaceChildren(...paragraphs.map(text => {
  const paragraph = document.createElement('p');
  paragraph.textContent = text;
  return paragraph;
 }));
 paytable.replaceChildren(...cascade.symbols.filter(symbol => symbol.payouts.some(amount => amount > 0)).map(symbol =>
  paytableRow(symbol.id, symbol.payouts.map(amount => amount > 0 ? amount + '×' : '—')),
 ));
}
$('#spin').onclick=event=>{if(busy||event.detail>1){finishCurrentSpin();return;}spin();};$('#boost').onclick=()=>spin(true);$('#auto').onclick=()=>{if(auto)stop();else{auto=true;update();if(!busy)spin();}};
for(const [id,delta] of [['#bet-minus',-1],['#bet-plus',1]])$(id).onclick=()=>{if(busy||auto||state?.freeSpinsRemaining||uncertain)return;bet=BET_POINTS[Math.max(0,Math.min(BET_POINTS.length-1,BET_POINTS.indexOf(bet)+delta))];update();};
const openRules=()=>{stop();modal=true;$('#rules').showModal();update();};$('#info').onclick=openRules;$('#rules-button').onclick=openRules;$('#close-rules').onclick=()=>$('#rules').close();$('#rules').addEventListener('close',()=>{modal=false;update();});
$('#sound').onclick=async()=>{const enabled=await audio.toggle();$('#sound').classList.toggle('sound-on',enabled);$('#sound').setAttribute('aria-label',enabled?'Mute sound':'Enable sound');};
$('#fullscreen').onclick=()=>document.fullscreenElement?document.exitFullscreen():document.documentElement.requestFullscreen().catch(()=>{});
document.addEventListener('visibilitychange',()=>{if(document.hidden)stop();});addEventListener('pagehide',stop);$('#canvas-host').addEventListener('renderer-lost',()=>{stop();ready=false;uncertain=true;finishCurrentSpin();$('#message').textContent='Graphics paused. Reload to recover your server balance.';update();});
document.addEventListener('keydown',e=>{if(e.code==='Space'&&!e.repeat&&!['BUTTON','INPUT','SELECT','A'].includes(document.activeElement?.tagName)){e.preventDefault();if(!auto){if(busy)finishCurrentSpin();else spin();}}});
init();
