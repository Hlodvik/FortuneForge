import './style.css';
import './fortune.css';
import './laptop.css';
import './fit-viewport.js';
import {callHost} from './bridge.js';
import {JadeRenderer} from './renderer.js';
import {LivingArtwork} from './living-art.js';
import {GameAudio} from './audio.js';
import {BET_POINTS,SYMBOLS,validateGrid} from './symbols.js';
const $=s=>document.querySelector(s),money=cents=>new Intl.NumberFormat('en-ZA',{style:'currency',currency:'ZAR'}).format(cents/100);
const audio=new GameAudio(),reduced=matchMedia('(prefers-reduced-motion:reduce)').matches;
const wait=ms=>new Promise(resolve=>setTimeout(resolve,reduced?80:ms));
let state=null,rules=null,balance=0,bet=20,busy=false,auto=false,ready=false,uncertain=false,win=0,timer,featureTotal=0,modal=false,instantRequested=false,skipCountdown=null;
const instantCheckbox=document.createElement('label');instantCheckbox.className='instant-results';instantCheckbox.innerHTML='<input id="instant-results" type="checkbox"> <span>2× speed</span>';$('#spin').after(instantCheckbox);
function finishCurrentSpin(){if(!busy)return;instantRequested=true;skipCountdown?.();renderer?.finishSpin();}
$('#instant-results').addEventListener('change',()=>{if(renderer)renderer.speed=$('#instant-results').checked?2:1;});
function countdown(){return new Promise(resolve=>{const done=()=>{clearTimeout(timeout);skipCountdown=null;resolve();};const timeout=setTimeout(done,reduced?80:450/(renderer?.speed||1));skipCountdown=done;});}
const artHost=document.createElement('div');artHost.className='fortune-arrival';$('#bonus-arrival').prepend(artHost);const art=new LivingArtwork(artHost);
let renderer;try{renderer=new JadeRenderer($('#canvas-host'));renderer.setGrid([['S01','S02','S03','S04'],['S04','S05','S06','S01'],['S07','S08','S01','S02'],['S05','S02','S04','S03'],['S08','S01','S03','S04']]);}catch{$('#message').textContent='3D could not start. Enable browser hardware acceleration and reload.';}
function update(){
 const free=state?.freeSpinsRemaining>0,points=free?state.freeSpinWagerPoints:bet;
 $('#balance').textContent=money(balance);$('#bet').textContent=money(points*25);$('#last-win').textContent=money(win);$('#round-total').textContent=money(free?featureTotal:win);
 $('#spin').disabled=!ready||modal||auto||uncertain;$('#spin-label').textContent=busy?'REVEAL':free?'FREE SPIN':'SPIN';$('#spin').setAttribute('aria-label',busy?'Show current spin result instantly':'Spin reels');$('#spin').setAttribute('aria-busy',String(busy));
 for(const id of ['#bet-minus','#bet-plus'])$(id).disabled=!ready||busy||free||auto||uncertain;
 $('#bet-minus').disabled||=BET_POINTS.indexOf(bet)===0;$('#bet-plus').disabled||=BET_POINTS.indexOf(bet)===BET_POINTS.length-1;
 $('#auto').disabled=!ready||uncertain;$('#auto-label').textContent=auto?'STOP AUTO':'AUTO';$('#auto').setAttribute('aria-pressed',String(auto));$('#auto').classList.toggle('active',auto);
 $('#jade-game').classList.toggle('bonus-active',free);$('#jade-game').dataset.bonus=state?.freeSpinFeatureMode?'wrath':free?'moon':'';
 $('#feature-name').textContent=state?.freeSpinFeatureMode?'DRAGON’S AWAKENING':free?'MOONLIT FREE GAMES':'JADE DRAGON';
 $('#feature-caption').textContent=free?`${state.freeSpinsRemaining} free games remaining · wager locked`:'23 paylines · Server-settled outcomes';
 $('#feature-meter-label').textContent=free?'SESSION FEATURE WIN':'ROUND WIN';$('#free-spins').textContent=free?`${state.freeSpinsRemaining} free spins remaining`:'Maximum bet R100';
 $('#free-games-badge').hidden=!free;$('#free-games-badge-count').textContent=`${state?.freeSpinsRemaining??0} LEFT`;
 $('#boost').disabled=!ready||busy||free||auto||uncertain||(state?.specialPointsBalance??0)<8;$('#special-count').textContent=state?.specialPointsBalance??0;
 if(free)showFree();else $('#free-games-popup').hidden=true;
 const collection=state?.sealCollections??[];$('#multiplier-ladder').innerHTML=['track1','track2','track3','track4'].map((id,i)=>{const p=collection.find(x=>x.sealId===id);return `<span class="fortune-seal"><b>${['GARDEN','MOON','DRAGON','GOLD'][i]}</b>${p?.count??0}/${p?.requiredCount??22}</span>`;}).join('');
}
function showFree(){if(!state?.freeSpinsRemaining)return;$('#free-popup-count').textContent=state.freeSpinsRemaining;$('#free-popup-title').textContent='FREE GAMES';$('#free-popup-caption').textContent='REMAINING';$('#free-games-popup').hidden=busy;}
function stop(){auto=false;clearTimeout(timer);update();}
async function init(){try{const data=await callHost('init');state=data.state;balance=data.balanceCents;rules=data.rules;bet=BET_POINTS.includes(bet)?bet:20;ready=Boolean(renderer);$('#loading').hidden=true;$('#message').textContent='Your Fortune Forge balance is connected. Spin when ready.';renderRules();update();}catch(error){$('#loading p').textContent=error.message;$('#message').textContent=error.message;}}
async function arrival(mode,count){const overlay=$('#bonus-arrival');overlay.dataset.theme=mode==='dragon'?'wrath':mode==='pearl'?'moon':'garden';$('#arrival-name').textContent=mode==='dragon'?'Dragon’s Awakening':'Free Games';$('#arrival-spins').textContent=`${count} FREE GAMES`;overlay.hidden=false;$('#free-games-popup').hidden=true;audio.feature(mode==='dragon'?'wrath':'moon');try{await art.start(mode);await wait(1400);}catch{await wait(300);}finally{art.stop();overlay.hidden=true;}}
async function spin(boost=false){
 if(!ready||busy||modal||uncertain)return;const free=state.freeSpinsRemaining>0,wager=free?state.freeSpinWagerPoints:bet;
 if(!free&&balance<wager*25){stop();$('#message').textContent='Your balance is too low for this bet.';return;}
 busy=true;instantRequested=false;renderer.speed=$('#instant-results').checked?2:1;win=0;$('#free-games-popup').hidden=true;update();audio.spin();
 try{
  if(free&&!instantRequested){$('#free-popup-count').textContent=state.freeSpinsRemaining;$('#free-games-popup').hidden=false;await countdown();$('#free-games-popup').hidden=true;}
  // No local outcome generation, stake subtraction, payout calculation or localStorage wallet.
  const result=await callHost('spin',{wagerPoints:wager,useFreeSpin:free,useSpecialBoost:boost});
  validateGrid(result.reels);state=result;balance=result.balanceCents;win=result.payout.totalPoints*result.pointValueInCents;
  if(free)featureTotal+=win;else featureTotal=0;
  await renderer.spin(result.reels,{instant:instantRequested});renderer.celebrate(result);audio.land(4);if(win>0)audio.win(win>=wager*25*20);
  $('#grid-accessible').textContent=result.reels.map((r,i)=>`Reel ${i+1}: ${r.map(id=>SYMBOLS[id][1]).join(', ')}`).join('. ');
  $('#message').textContent=win>0?`${money(win)} won · confirmed by Fortune Forge`:'Round complete. Your next spin is up to you.';
  if(result.freeSpinsRemaining>0&&(!free||result.freeSpinsAwarded>0)){await arrival(result.freeSpinFeatureMode?'dragon':'pearl',result.freeSpinsRemaining);}
 }catch(error){stop();uncertain=true;$('#message').textContent=`${error.message} Reload to recover the server balance; this request will not be repeated automatically.`;}
 finally{busy=false;update();if(auto&&!document.hidden&&!uncertain)timer=setTimeout(()=>spin(),win?900:500);}
}
function renderRules(){const host=$('#paytable');host.innerHTML='';for(const symbol of rules.symbols){if(!symbol.paylinePayoutRules.length)continue;const row=document.createElement('div');row.className='paytable-row';for(const text of [SYMBOLS[symbol.symbolId]?.[1]??symbol.symbolId,...[3,4,5].map(n=>symbol.paylinePayoutRules.find(p=>p.matchLength===n)?.multiplier??'—')]){const node=document.createElement('span');node.textContent=text;row.append(node);}host.append(row);}}
$('#spin').onclick=event=>{if(busy||event.detail>1){finishCurrentSpin();return;}spin();};$('#boost').onclick=()=>spin(true);$('#auto').onclick=()=>{if(auto)stop();else{auto=true;update();if(!busy)spin();}};
for(const [id,delta] of [['#bet-minus',-1],['#bet-plus',1]])$(id).onclick=()=>{if(busy||auto||state?.freeSpinsRemaining||uncertain)return;bet=BET_POINTS[Math.max(0,Math.min(BET_POINTS.length-1,BET_POINTS.indexOf(bet)+delta))];update();};
const openRules=()=>{stop();modal=true;$('#rules').showModal();update();};$('#info').onclick=openRules;$('#rules-button').onclick=openRules;$('#close-rules').onclick=()=>$('#rules').close();$('#rules').addEventListener('close',()=>{modal=false;update();});
$('#sound').onclick=async()=>{const enabled=await audio.toggle();$('#sound').classList.toggle('sound-on',enabled);$('#sound').setAttribute('aria-label',enabled?'Mute sound':'Enable sound');};
$('#fullscreen').onclick=()=>document.fullscreenElement?document.exitFullscreen():document.documentElement.requestFullscreen().catch(()=>{});
document.addEventListener('visibilitychange',()=>{if(document.hidden)stop();});addEventListener('pagehide',stop);$('#canvas-host').addEventListener('renderer-lost',()=>{stop();ready=false;uncertain=true;$('#message').textContent='Graphics paused. Reload to recover your server balance.';update();});
document.addEventListener('keydown',e=>{if(e.code==='Space'&&!e.repeat&&!['BUTTON','INPUT','SELECT','A'].includes(document.activeElement?.tagName)){e.preventDefault();if(!auto)spin();}});
init();
