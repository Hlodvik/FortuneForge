import { mkdir, copyFile } from 'node:fs/promises'
import { resolve } from 'node:path'
import { expect, test, type Page, type TestInfo, type Locator } from '@playwright/test'
import type { VideoPokerCard, VideoPokerRound, VideoPokerStatus, VideoPokerHandCount, VideoPokerHandRank } from '../../../games/VideoPoker/client/FortuneForge.Games.VideoPoker.Client/src/contracts'

const player = 'video-poker-ux-player:account'
const pendingKey = 'fortuneforge:video-poker:pending:' + player
const roundKey = 'fortuneforge:video-poker:round:' + player
const holdsKey = 'fortuneforge:video-poker:holds:' + player
const card = (rank: VideoPokerCard['rank'], suit: VideoPokerCard['suit']): VideoPokerCard => ({ rank, suit })
const initial = [card('ace','clubs'), card('ace','spades'), card('five','spades'), card('six','hearts'), card('nine','diamonds')]
const trip = [initial[0], initial[1], card('ace','hearts'), card('seven','diamonds'), card('eight','clubs')]
const royal = ['ten','jack','queen','king','ace'].map(rank => card(rank as VideoPokerCard['rank'], 'spades'))
const lowPair = [card('five','clubs'), card('five','diamonds'), card('six','spades'), card('eight','hearts'), card('king','clubs')]
const status: VideoPokerStatus = { available: true, minimumCoinsWagered: 1, maximumCoinsWagered: 5, coinValue: 1, balance: 1000, handCounts: [1,3,5] }
const viewports = [
  { name: 'desktop', width: 1280, height: 720 }, { name: 'phone', width: 390, height: 844 },
  { name: 'short-phone', width: 390, height: 700 }, { name: 'compact-phone', width: 320, height: 568 },
  { name: 'narrow-landscape', width: 500, height: 320 }, { name: 'small-landscape', width: 568, height: 320 },
  { name: 'landscape', width: 667, height: 375 }, { name: 'wide-landscape', width: 852, height: 393 },
] as const

for (const viewport of viewports) {
  test('Video Poker '+viewport.name+' fits one, three and five hands with fixed actions and optional panels', async ({ page }, info) => {
    await page.setViewportSize(viewport)
    await page.emulateMedia({ reducedMotion: 'reduce' })
    const fixture = await mockPoker(page)
    await page.goto('/games/video-poker')
    const deal = page.getByRole('button', { name: 'Deal', exact: true })
    await expect(deal).toBeEnabled()
    await page.evaluate(() => document.fonts.ready)
    await fits(page, viewport)
    await capture(page, info, viewport.name+'-betting')
    const originalBoard = await page.locator('.ff-video-poker__screen').boundingBox()
    const originalAction = await deal.boundingBox()
    for (const count of [1,3,5] as const) {
      await page.getByRole('button', { name: String(count), exact: true }).click()
      await deal.click()
      await expect(page.getByRole('button', { name: 'Draw', exact: true })).toBeEnabled()
      const first = page.getByRole('button', { name: 'Hold ace of clubs', exact: true })
      const firstBox = await first.boundingBox()
      await first.click()
      await page.getByRole('button', { name: 'Hold ace of spades', exact: true }).click()
      await expect(page.getByRole('button', { name: 'Release ace of clubs' })).toHaveAttribute('aria-pressed', 'true')
      expect(await page.getByRole('button', { name: 'Release ace of clubs' }).boundingBox()).toEqual(firstBox)
      expect(await page.locator('.ff-video-poker__screen').boundingBox()).toEqual(originalBoard)
      sameBox(await page.getByRole('button', { name: 'Draw', exact: true }).boundingBox(), originalAction)
      await fits(page, viewport)
      await capture(page, info, viewport.name+'-'+count+'-held')
      await page.getByRole('button', { name: 'Draw', exact: true }).click()
      await expect(page.getByRole('button', { name: 'Deal Again', exact: true })).toBeEnabled()
      await expect(page.locator('.ff-video-poker__completed-hand')).toHaveCount(count)
      await expect(page.locator('.ff-video-poker__board .ff-video-poker__card')).toHaveCount(count*5)
      await expect(page.locator('.ff-video-poker__round-head')).toHaveText('R'+(count*3).toFixed(2)+' total won')
      expect(await page.locator('.ff-video-poker__screen').boundingBox()).toEqual(originalBoard)
      sameBox(await page.getByRole('button', { name: 'Deal Again', exact: true }).boundingBox(), originalAction)
      await expect(page.locator('.ff-video-poker__board .ff-video-poker__card').first()).toHaveCSS('animation-name','none')
      await fits(page, viewport)
      await capture(page, info, viewport.name+'-'+count+'-result')
      if (count===5) {
        await page.getByRole('button', { name: 'Video Poker round details', exact: true }).click()
        const panel=page.getByRole('dialog', { name: 'Video Poker round details', exact: true })
        await expect(panel).toContainText('WagerR5.00Total returnR15.00NetR10.00')
        await fitsPanel(panel, viewport)
        await capture(page, info, viewport.name+'-details')
        await page.keyboard.press('Escape')
        await expect(page.getByRole('button', { name:'Video Poker round details',exact:true })).toBeFocused()
      }
      await page.getByRole('button', { name: 'New Hand', exact: true }).click()
      await expect(page.getByRole('combobox', { name:'Coin wager' })).toHaveValue('1')
      await expect(page.getByRole('combobox', { name:'Coin wager' })).toBeFocused()
      await expect(page.locator('.ff-video-poker__balance strong')).toHaveText('R'+fixture.balance.toLocaleString('en-US',{minimumFractionDigits:2,maximumFractionDigits:2}))
    }
    await page.getByRole('combobox',{name:'Coin wager'}).selectOption('5')
    await page.getByRole('button',{name:'Video Poker paytable',exact:true}).click()
    const paytable=page.getByRole('dialog',{name:'Video Poker paytable',exact:true})
    await expect(paytable).toContainText('Royal Flush4,000 coins')
    await fitsPanel(paytable,viewport)
    await capture(page,info,viewport.name+'-paytable')
    await page.keyboard.press('Escape')
    await expect(page.getByRole('button',{name:'Video Poker paytable',exact:true})).toBeFocused()
    await page.getByRole('button',{name:'How to play Video Poker',exact:true}).click()
    await fitsPanel(page.getByRole('dialog'),viewport)
    await capture(page,info,viewport.name+'-help')
    await page.keyboard.press('Escape')
  })
}

test('Video Poker keyboard holds, reload draft, exact repeat and selected-coin paytable remain coherent', async ({ page }, info) => {
  const fixture=await mockPoker(page)
  await page.goto('/games/video-poker')
  await page.getByRole('combobox',{name:'Coin wager'}).selectOption('3')
  await page.getByRole('button',{name:'3',exact:true}).click()
  await page.getByRole('button',{name:'Deal',exact:true}).click()
  await expect(page.getByRole('button',{name:'Draw',exact:true})).toBeEnabled()
  await page.getByRole('button',{name:'Hold ace of clubs'}).focus()
  await page.keyboard.press('1')
  await page.keyboard.press('2')
  await expect(page.getByRole('button',{name:'Release ace of clubs'})).toBeFocused()
  await expect(page.getByRole('button',{name:'Release ace of spades'})).toHaveAttribute('aria-pressed','true')
  await page.reload()
  await expect(page.getByRole('button',{name:'Draw',exact:true})).toBeEnabled()
  await expect(page.getByRole('button',{name:'Release ace of clubs'})).toHaveAttribute('aria-pressed','true')
  await expect(page.getByRole('button',{name:'Release ace of spades'})).toHaveAttribute('aria-pressed','true')
  await capture(page,info,'reload-hold-draft')
  await page.getByRole('button',{name:'Video Poker basic guide',exact:true}).click()
  await page.getByRole('button',{name:'Strategy Off',exact:true}).click()
  await expect(page.getByRole('dialog')).toContainText('ace of clubs, ace of spades')
  await page.getByRole('button',{name:'How to play Video Poker',exact:true}).click()
  await expect(page.getByRole('dialog')).toHaveCount(1)
  await page.keyboard.press('Escape')
  await expect(page.getByRole('button',{name:'How to play Video Poker',exact:true})).toBeFocused()
  await page.getByRole('button',{name:'Draw',exact:true}).click()
  await expect(page.getByRole('button',{name:'Deal Again',exact:true})).toBeEnabled()
  await expect(page.locator('.ff-video-poker__round-head')).toHaveText('R27.00 total won')
  await expect(page.locator('.ff-video-poker__inline-paytable .is-winning-row')).toContainText('Three of a Kind9 coins')
  expect(await page.evaluate(key=>sessionStorage.getItem(key),holdsKey)).toBeNull()
  await page.getByRole('button',{name:'Deal Again',exact:true}).click()
  await expect(page.getByRole('button',{name:'Draw',exact:true})).toBeEnabled()
  const deals=fixture.requests.filter(r=>r.operation==='deal')
  expect(deals[1].body).toEqual({coinsWagered:3,handCount:3})
  expect(deals[1].body).toEqual(deals[0].body)
  expect(deals[1].key).not.toBe(deals[0].key)
  await capture(page,info,'repeat-same-coins-hands')
})

test('Video Poker losing low pair is not advertised as a paying high pair, royal shows authoritative payout', async ({ page },info) => {
  const fixture=await mockPoker(page,{result:'low-pair'})
  await page.goto('/games/video-poker')
  await page.getByRole('button',{name:'Deal',exact:true}).click()
  await page.getByRole('button',{name:'Draw',exact:true}).click()
  await expect(page.getByRole('button',{name:'Deal Again',exact:true})).toBeEnabled()
  await expect(page.locator('.ff-video-poker__completed-hand header strong')).toHaveText('Pair')
  await expect(page.locator('.ff-video-poker__round-head')).toHaveText('No win')
  await expect(page.locator('.is-winning-row')).toHaveCount(0)
  await capture(page,info,'low-pair-no-win')
  fixture.result='royal'
  await page.getByRole('button',{name:'New Hand',exact:true}).click()
  await page.getByRole('button',{name:'Bet Max & Deal',exact:true}).click()
  await page.getByRole('button',{name:'Draw',exact:true}).click()
  await expect(page.getByRole('button',{name:'Deal Again',exact:true})).toBeEnabled()
  await expect(page.locator('.ff-video-poker__round-head')).toHaveText('R4,000.00 total won')
  await expect(page.locator('.ff-video-poker__inline-paytable .is-winning-row')).toContainText('Royal Flush4,000 coins')
  await capture(page,info,'five-coin-royal')
})

test('Video Poker supported hand choices and affordability prevent unavailable or unaffordable deals', async ({ page },info) => {
  const fixture=await mockPoker(page,{status:{handCounts:[3],coinValue:2,balance:7}})
  await page.goto('/games/video-poker')
  await expect(page.getByRole('group',{name:'Hands'}).getByRole('button')).toHaveCount(1)
  await expect(page.getByRole('button',{name:'3',exact:true})).toHaveAttribute('aria-pressed','true')
  await expect(page.getByRole('button',{name:'Deal',exact:true})).toBeEnabled()
  await expect(page.getByRole('button',{name:'Bet Max & Deal',exact:true})).toBeDisabled()
  await page.getByRole('combobox',{name:'Coin wager'}).selectOption('2')
  await expect(page.getByRole('button',{name:'Deal',exact:true})).toBeDisabled()
  expect(fixture.requests).toHaveLength(0)
  await page.getByRole('combobox',{name:'Coin wager'}).selectOption('1')
  await page.getByRole('button',{name:'Deal',exact:true}).click()
  await expect(page.getByRole('button',{name:'Draw',exact:true})).toBeEnabled()
  await expect(page.locator('.ff-video-poker__ticket')).toHaveText('1 coin × 3 hands · R6.00')
  await page.getByRole('button',{name:'Draw',exact:true}).click()
  await expect(page.getByRole('button',{name:'New Hand',exact:true})).toBeEnabled()
  await expect(page.locator('.ff-video-poker__round-head')).toHaveText('R18.00 total won')
  await capture(page,info,'restricted-hands-nonunit-coin')
})

test('Video Poker interrupted deal and draw reuse exact keys and lock stake/holds', async ({ page },info) => {
  const fixture=await mockPoker(page,{abortDeal:true,abortDraw:true})
  await page.goto('/games/video-poker')
  await page.getByRole('combobox',{name:'Coin wager'}).selectOption('3')
  await page.getByRole('button',{name:'5',exact:true}).click()
  await page.getByRole('button',{name:'Deal',exact:true}).click()
  await expect(page.getByRole('button',{name:'Retry Deal',exact:true})).toBeEnabled()
  await expect(page.getByRole('combobox',{name:'Coin wager'})).toBeDisabled()
  await expect(page.getByRole('button',{name:'Bet 1',exact:true})).toBeDisabled()
  await expect(page.getByRole('button',{name:'Bet Max & Deal',exact:true})).toBeDisabled()
  await capture(page,info,'interrupted-deal')
  await page.getByRole('button',{name:'Retry Deal',exact:true}).click()
  await page.getByRole('button',{name:'Draw',exact:true}).waitFor()
  const deals=fixture.requests.filter(r=>r.operation==='deal')
  expect(deals[1]).toEqual(deals[0])
  await page.getByRole('button',{name:'Hold ace of clubs'}).click()
  await page.getByRole('button',{name:'Hold ace of spades'}).click()
  await page.getByRole('button',{name:'Draw',exact:true}).click()
  await expect(page.getByRole('button',{name:'Retry Draw',exact:true})).toBeEnabled()
  await expect(page.getByRole('button',{name:'Release ace of clubs'})).toBeDisabled()
  await capture(page,info,'interrupted-draw')
  await page.reload()
  await expect(page.getByRole('button',{name:'Deal Again',exact:true})).toBeEnabled()
  const draws=fixture.requests.filter(r=>r.operation==='draw')
  expect(draws.length).toBeGreaterThanOrEqual(2)
  expect(draws.every(r=>JSON.stringify(r)===JSON.stringify(draws[0]))).toBe(true)
  expect(fixture.balance).toBe(1030)
  expect(await page.evaluate(key=>sessionStorage.getItem(key),pendingKey)).toBeNull()
  await capture(page,info,'reload-pending-draw')
})

test('Video Poker restoration and status failures remain independently retryable without new deal', async ({ page },info) => {
  await page.setViewportSize({width:320,height:568})
  await page.addInitScript(({key,id})=>sessionStorage.setItem(key,id),{key:roundKey,id:'restored-round'})
  const fixture=await mockPoker(page,{failStatus:100,failRestore:100})
  fixture.round=makeRound('restored-round',1,3,997)
  await page.goto('/games/video-poker')
  await expect(page.getByRole('button',{name:'Retry restoration',exact:true})).toBeEnabled()
  await expect(page.getByRole('button',{name:'Retry connection',exact:true})).toBeEnabled()
  await fits(page,{width:320,height:568},false)
  await capture(page,info,'compact-combined-recovery-errors')
  fixture.failStatus=0
  await page.getByRole('button',{name:'Retry connection',exact:true}).click()
  await expect(page.getByRole('button',{name:'Retry restoration',exact:true})).toBeEnabled()
  await expect(page.getByRole('button',{name:'Deal',exact:true})).toHaveCount(0)
  await fits(page,{width:320,height:568},false)
  await capture(page,info,'compact-independent-recovery-error')
  fixture.failRestore=0
  await page.getByRole('button',{name:'Retry restoration',exact:true}).click()
  await expect(page.getByRole('button',{name:'Draw',exact:true})).toBeEnabled()
  await expect(page.getByRole('alert')).toHaveCount(0)
  expect(fixture.requests.filter(r=>r.operation==='deal')).toHaveLength(0)
  await capture(page,info,'recovered-unfinished-hand')
})

test('Video Poker expired round clears stale marker and deliberately closed table has no retry loop', async ({ page }) => {
  await page.addInitScript(({key,id})=>sessionStorage.setItem(key,id),{key:roundKey,id:'expired'})
  await mockPoker(page)
  await page.goto('/games/video-poker')
  await expect(page.getByRole('button',{name:'Deal',exact:true})).toBeEnabled()
  expect(await page.evaluate(key=>sessionStorage.getItem(key),roundKey)).toBeNull()
  await page.route('**/api/games/video-poker/status',route=>route.fulfill({status:503,json:{code:'video-poker-disabled',message:'Closed'}}))
  await page.reload()
  await expect(page.getByRole('alert')).toContainText('temporarily unavailable')
  await expect(page.getByRole('button',{name:'Retry connection',exact:true})).toHaveCount(0)
  await expect(page.getByRole('button',{name:'Deal',exact:true})).toHaveCount(0)
})

test('Video Poker all-held draw settles without flashing a premature result or reanimating held cards', async ({ page },info) => {
  const fixture=await mockPoker(page)
  await page.goto('/games/video-poker')
  await page.getByRole('button',{name:'Deal',exact:true}).click()
  await expect(page.getByRole('button',{name:'Draw',exact:true})).toBeEnabled()
  for (const button of await page.locator('.ff-video-poker__card-button').all()) await button.click()
  await page.getByRole('button',{name:'Draw',exact:true}).click()
  await expect(page.getByRole('button',{name:'Deal Again',exact:true})).toBeEnabled()
  await expect(page.locator('.ff-video-poker__completed-hand header strong')).toHaveText('Jacks or Better')
  await expect(page.locator('.ff-video-poker__board .ff-video-poker__card[data-new="true"]')).toHaveCount(0)
  expect(fixture.requests.find(r=>r.operation==='draw')!.body).toEqual({heldPositions:[0,1,2,3,4]})
  await capture(page,info,'all-five-held')
})

test('Video Poker normal-motion reveal retains pending marker, delays wallet/result, and survives mid-draw reload', async ({ page },info) => {
  await page.emulateMedia({reducedMotion:'no-preference'})
  await page.addInitScript(() => {
    const frames:{time:number;cards:number;complete:boolean;pending:boolean;balance:string|null}[]=[]
    Reflect.set(window,'vpFrames',frames)
    new MutationObserver(()=>{
      const main=document.querySelector('.ff-video-poker')
      if(!main)return
      const cards=main.querySelectorAll('.ff-video-poker__board .ff-video-poker__card:not(.ff-video-poker__card--back)').length
      const complete=[...main.querySelectorAll('button')].some(b=>b.textContent==='Deal Again')
      const balance=main.querySelector('.ff-video-poker__balance strong')?.textContent??null
      if(frames.at(-1)?.cards!==cards || frames.at(-1)?.complete!==complete || frames.at(-1)?.balance!==balance)
        frames.push({time:performance.now(),cards,complete,balance,pending:sessionStorage.getItem('fortuneforge:video-poker:pending:video-poker-ux-player:account')!==null})
    }).observe(document,{subtree:true,childList:true})
  })
  const fixture=await mockPoker(page)
  await page.goto('/games/video-poker')
  await page.getByRole('button',{name:'Deal',exact:true}).click()
  await expect(page.getByRole('button',{name:'Draw',exact:true})).toBeEnabled()
  await page.getByRole('button',{name:'Hold ace of clubs'}).click()
  await page.getByRole('button',{name:'Hold ace of spades'}).click()
  await page.getByRole('button',{name:'Draw',exact:true}).click()
  await expect(page.locator('.ff-video-poker__completed-hand .ff-video-poker__card[data-new="true"]')).toHaveCount(1)
  expect(await page.evaluate(key=>sessionStorage.getItem(key),pendingKey)).not.toBeNull()
  await expect(page.locator('.ff-video-poker__balance strong')).toHaveText('R999.00')
  await page.reload()
  await expect(page.getByRole('button',{name:'Deal Again',exact:true})).toBeEnabled()
  const frames=await page.evaluate(()=>Reflect.get(window,'vpFrames')) as {time:number;cards:number;complete:boolean;pending:boolean;balance:string|null}[]
  const last=frames.find(f=>f.cards===5&&!f.complete)!
  const finished=frames.find(f=>f.cards===5&&f.complete)!
  expect(last).toBeTruthy()
  expect(last.pending).toBe(true)
  expect(finished.time-last.time).toBeGreaterThanOrEqual(145)
  await expect(page.locator('.ff-video-poker__balance strong')).toHaveText('R1,002.00')
  await expect(page.locator('.ff-video-poker__card[data-new="true"]').first()).toHaveCSS('opacity','1')
  expect(await page.evaluate(key=>sessionStorage.getItem(key),pendingKey)).toBeNull()
  const draws=fixture.requests.filter(r=>r.operation==='draw')
  expect(draws.length).toBeGreaterThanOrEqual(2)
  expect(draws.every(r=>JSON.stringify(r)===JSON.stringify(draws[0]))).toBe(true)
  await capture(page,info,'normal-motion-recovered-draw')
})

function makeRound(id:string,coins:number,hands:VideoPokerHandCount,balance:number):VideoPokerRound {
  return {roundId:id,balance,coinsWagered:coins,handCount:hands,wager:coins*hands,phase:'awaiting-draw',initialCards:initial,heldPositions:[],finalCards:null,handRank:null,payout:null,finalHands:null,handRanks:null,handPayouts:null}
}
async function mockPoker(page:Page,options:{status?:Partial<VideoPokerStatus>;result?:'low-pair'|'royal'|'mixed';abortDeal?:boolean;abortDraw?:boolean;rejectDeal?:boolean;failStatus?:number;failRestore?:number}={}) {
  const fixture={balance:options.status?.balance??1000,round:null as VideoPokerRound|null,result:options.result??'trip',requests:[] as {operation:string;key:string;body:unknown}[],cache:new Map<string,VideoPokerRound>(),serial:0,rejectDeal:options.rejectDeal??false,abortDeal:options.abortDeal??false,abortDraw:options.abortDraw??false,failStatus:options.failStatus??0,failRestore:options.failRestore??0}
  await page.route('**/api/**',async route=>{
    const req=route.request(),path=new URL(req.url()).pathname
    const send=(json:unknown,statusCode=200)=>route.fulfill({status:statusCode,json})
    if(path==='/api/accounts/me') return send({userId:'video-poker-ux-player',playerName:'Alex',email:'vp@example.test',createdAtUtc:'2026-01-01T00:00:00Z',balances:{slotsCredits:fixture.balance,freeGames:0},role:'Player',slots:{spinsPlayed:0,wins:0,losses:0,creditsWagered:0,creditsWon:0,netCredits:0}})
    if(path.endsWith('/status')) {if(fixture.failStatus-->0) return send({message:'Offline',code:'offline'},503);return send({...status,...options.status,balance:fixture.balance})}
    if(path.endsWith('/rounds')&&req.method()==='POST') {
      const body=req.postDataJSON(),key=req.headers()['idempotency-key']
      fixture.requests.push({operation:'deal',key,body})
      if(fixture.rejectDeal){fixture.rejectDeal=false;fixture.balance=7;return send({code:'insufficient-slot-credits',message:'Not enough current credits.'},409)}
      if(!fixture.cache.has(key)) {
        const wager=body.coinsWagered*body.handCount*(options.status?.coinValue??1)
        fixture.balance-=wager
        fixture.round={...makeRound('vp-'+(++fixture.serial),body.coinsWagered,body.handCount,fixture.balance),wager}
        fixture.cache.set(key,fixture.round)
      }
      if(fixture.abortDeal){fixture.abortDeal=false;return route.abort('connectionreset')}
      return send(fixture.cache.get(key))
    }
    if(path.endsWith('/draw')&&req.method()==='POST') {
      const body=req.postDataJSON(),key=req.headers()['idempotency-key']
      fixture.requests.push({operation:'draw',key,body})
      if(!fixture.cache.has(key)) {
        const current=fixture.round!,all=body.heldPositions.length===5
        const cards=all?initial:fixture.result==='low-pair'?lowPair:fixture.result==='royal'?royal:trip
        const rank:VideoPokerHandRank=all||fixture.result==='low-pair'?'pair':fixture.result==='royal'?'royal-flush':'three-of-a-kind'
        const coins=all?current.coinsWagered:fixture.result==='low-pair'?0:fixture.result==='royal'?(current.coinsWagered===5?4000:250*current.coinsWagered):3*current.coinsWagered
        const perHand=coins*(options.status?.coinValue??1)
        const finalHands=fixture.result==='mixed'?[trip,initial,lowPair,royal,[initial[0],initial[1],initial[2],card('five','hearts'),initial[4]]].slice(0,current.handCount):Array.from({length:current.handCount},()=>cards)
        const handRanks:VideoPokerHandRank[]=fixture.result==='mixed'?(['three-of-a-kind','pair','pair','royal-flush','two-pair'] as VideoPokerHandRank[]).slice(0,current.handCount):Array.from({length:current.handCount},()=>rank)
        const handPayouts=fixture.result==='mixed'?[3,1,0,250,2].slice(0,current.handCount).map(p=>p*current.coinsWagered*(options.status?.coinValue??1)):Array.from({length:current.handCount},()=>perHand)
        const payout=handPayouts.reduce((sum,p)=>sum+p,0)
        fixture.balance+=payout
        fixture.round={...current,balance:fixture.balance,phase:'completed',heldPositions:body.heldPositions,finalCards:finalHands[0],handRank:handRanks[0],payout,finalHands,handRanks,handPayouts}
        fixture.cache.set(key,fixture.round)
      }
      if(fixture.abortDraw){fixture.abortDraw=false;return route.abort('connectionreset')}
      return send(fixture.cache.get(key))
    }
    if(path.includes('/rounds/')&&req.method()==='GET') {if(fixture.failRestore-->0)return send({message:'Recovery offline'},503);return fixture.round?send(fixture.round):send({message:'Expired'},404)}
    return send({})
  })
  return fixture
}
async function capture(page:Page,info:TestInfo,name:string) {const path=info.outputPath(name+'.png');await page.screenshot({path});await info.attach(name,{path,contentType:'image/png'});const evidence=resolve('../.artifacts/ux-cycle/video-poker');await mkdir(evidence,{recursive:true});await copyFile(path,resolve(evidence,name+'.png'))}
async function fitsPanel(panel:Locator,viewport:{width:number;height:number}) {const box=(await panel.boundingBox())!;expect(box.x).toBeGreaterThanOrEqual(0);expect(box.y).toBeGreaterThanOrEqual(0);expect(box.x+box.width).toBeLessThanOrEqual(viewport.width+1);expect(box.y+box.height).toBeLessThanOrEqual(viewport.height+1)}
async function fits(page:Page,viewport:{width:number;height:number},cards=true) {
  const overflow=await page.evaluate(()=>({width:document.documentElement.scrollWidth,height:document.documentElement.scrollHeight}))
  expect(overflow.width).toBeLessThanOrEqual(viewport.width+1);expect(overflow.height).toBeLessThanOrEqual(viewport.height+1)
  const board=(await page.locator('.ff-video-poker__screen').boundingBox())!
  for(const node of await page.locator('.ff-video-poker__rail button,.ff-video-poker__rail select').all()) {
    if(!await node.isVisible())continue
    const box=(await node.boundingBox())!;expect(box.x).toBeGreaterThanOrEqual(0);expect(box.y).toBeGreaterThanOrEqual(0);expect(box.x+box.width).toBeLessThanOrEqual(viewport.width+1);expect(box.y+box.height).toBeLessThanOrEqual(viewport.height+1);expect(box.height).toBeGreaterThanOrEqual(43)
  }
  if(cards)for(const node of await page.locator('.ff-video-poker__board .ff-video-poker__card').all()) {
    const box=(await node.boundingBox())!;expect(box.width).toBeGreaterThanOrEqual(20);expect(box.height).toBeGreaterThanOrEqual(28)
    expect(box.x).toBeGreaterThanOrEqual(board.x-1);expect(box.x+box.width).toBeLessThanOrEqual(board.x+board.width+1);expect(box.y).toBeGreaterThanOrEqual(board.y-1);expect(box.y+box.height).toBeLessThanOrEqual(board.y+board.height+1)
  }
}

function sameBox(actual:{x:number;y:number;width:number;height:number}|null, expected:{x:number;y:number;width:number;height:number}|null) { expect(actual).toBeTruthy(); expect(expected).toBeTruthy(); for(const key of ['x','y','width','height'] as const) expect(Math.abs(actual![key]-expected![key])).toBeLessThan(1); }

test('Video Poker mixed five-hand results identify each category/return and only highlight paying rows',async({page},info)=>{
  await mockPoker(page,{result:'mixed'})
  await page.goto('/games/video-poker')
  await page.getByRole('button',{name:'5',exact:true}).click()
  await page.getByRole('button',{name:'Deal',exact:true}).click()
  await page.getByRole('button',{name:'Draw',exact:true}).click()
  await expect(page.getByRole('button',{name:'Deal Again',exact:true})).toBeEnabled()
  await expect(page.locator('.ff-video-poker__completed-hand header strong')).toHaveText(['Three of a Kind','Jacks or Better','Pair','Royal Flush','Two Pair'])
  await expect(page.locator('.ff-video-poker__completed-hand header em')).toHaveText(['R3.00','R1.00','R0.00','R250.00','R2.00'])
  await expect(page.locator('.ff-video-poker__round-head')).toHaveText('R256.00 total won')
  await expect(page.locator('.ff-video-poker__completed-hand.is-winning')).toHaveCount(4)
  await expect(page.locator('.ff-video-poker__inline-paytable .is-winning-row')).toHaveCount(4)
  await capture(page,info,'mixed-five-hand-returns')
})

test('Video Poker definitive no-round rejection refreshes wallet and lets the player correct the wager',async({page},info)=>{
  const fixture=await mockPoker(page,{rejectDeal:true})
  await page.goto('/games/video-poker')
  await page.getByRole('combobox',{name:'Coin wager'}).selectOption('3')
  await page.getByRole('button',{name:'5',exact:true}).click()
  await page.getByRole('button',{name:'Deal',exact:true}).click()
  await expect(page.getByRole('alert')).toContainText('Not enough current credits')
  await expect(page.getByRole('combobox',{name:'Coin wager'})).toBeEnabled()
  await expect(page.getByRole('button',{name:'Deal',exact:true})).toBeDisabled()
  await expect(page.locator('.ff-video-poker__balance strong')).toHaveText('R7.00')
  await expect(page.getByLabel('7 South African rand',{exact:true})).toBeVisible()
  expect(await page.evaluate(key=>sessionStorage.getItem(key),pendingKey)).toBeNull()
  await capture(page,info,'confirmed-rejection-edit-wager')
  await page.getByRole('combobox',{name:'Coin wager'}).selectOption('1')
  await page.getByRole('button',{name:'Deal',exact:true}).click()
  await expect(page.getByRole('button',{name:'Draw',exact:true})).toBeEnabled()
  const deals=fixture.requests.filter(r=>r.operation==='deal')
  expect(deals[0].body).toEqual({coinsWagered:3,handCount:5})
  expect(deals[1].body).toEqual({coinsWagered:1,handCount:5})
  expect(deals[1].key).not.toBe(deals[0].key)
  await capture(page,info,'confirmed-rejection-corrected-deal')
})

test('Video Poker touch emulation keeps card holds, draw and repeat within the first viewport',async({browser},info)=>{
  const viewport={width:390,height:844}
  const context=await browser.newContext({viewport,hasTouch:true,isMobile:true,baseURL:'http://127.0.0.1:4187',serviceWorkers:'block',reducedMotion:'reduce'})
  const page=await context.newPage()
  try {
    await mockPoker(page)
    await page.goto('/games/video-poker')
    await page.getByRole('button',{name:'5',exact:true}).tap()
    await page.getByRole('button',{name:'Deal',exact:true}).tap()
    await expect(page.getByRole('button',{name:'Draw',exact:true})).toBeEnabled()
    await page.getByRole('button',{name:'Hold ace of clubs'}).tap()
    await page.getByRole('button',{name:'Hold ace of spades'}).tap()
    await expect(page.getByRole('button',{name:'Release ace of clubs'})).toHaveAttribute('aria-pressed','true')
    await expect(page.getByRole('button',{name:'Release ace of clubs'})).toHaveCSS('user-select','none')
    await expect(page.locator('.ff-video-poker__board img')).toHaveCount(0)
    await fits(page,viewport)
    await capture(page,info,'touch-held')
    await page.getByRole('button',{name:'Draw',exact:true}).tap()
    await expect(page.getByRole('button',{name:'Deal Again',exact:true})).toBeEnabled()
    await fits(page,viewport)
    await capture(page,info,'touch-five-hand-result')
    await page.getByRole('button',{name:'Deal Again',exact:true}).tap()
    await expect(page.getByRole('button',{name:'Draw',exact:true})).toBeEnabled()
  } finally {await context.close()}
})
