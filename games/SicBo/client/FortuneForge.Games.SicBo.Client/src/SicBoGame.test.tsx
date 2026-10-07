// @vitest-environment jsdom
import { act, cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { SicBoGame } from './SicBoGame'
import { SicBoGatewayError, type SicBoBetRequest, type SicBoGateway, type SicBoRound, type SicBoStatus } from './contracts'
import { playSicBoSound } from './sicBoAudio'
import { betTargets, createTargetBet, historyStorageKey, pendingStorageKey } from './sicBoPresentation'

vi.mock('./sicBoAudio',()=>({playSicBoSound:vi.fn()}))

// Independent server-hash fixtures use the test runtime's crypto; this browser package has no Node typings.
const { createHash, webcrypto } = await import('node:' + 'crypto') as {
  webcrypto: Crypto
  createHash: (algorithm: string) => { update: (value: string) => { digest: (encoding: 'hex') => string } }
}
type Mode = 'account'|'practice'
const status: SicBoStatus = { available: true, minimumStake: 1, maximumStakePerBet: 100, stakeIncrement: 1, maximumBetsPerRound: 20, balance: 100, mode: 'three-dice-sic-bo' }
const owner='owner-7', savedKey='sic-bo-recovery-0001', uuid='4e9c20fa-1e81-455f-b3e9-26dd0c31c543', defaultId='a1'.repeat(32)
const small=createTargetBet('small',1)!, totalNine=createTargetBet('total-9',1)!, combination=createTargetBet('two-number-combination-2-5',1)!
beforeEach(()=>{
  vi.mocked(playSicBoSound).mockClear()
  vi.stubGlobal('crypto',{subtle:webcrypto.subtle,randomUUID:()=>uuid}); motion(true)
  Object.defineProperty(HTMLDialogElement.prototype,'showModal',{configurable:true,value(this:HTMLDialogElement){this.setAttribute('open','')}})
  Object.defineProperty(HTMLDialogElement.prototype,'close',{configurable:true,value(this:HTMLDialogElement){this.removeAttribute('open')}})
})
afterEach(()=>{cleanup();sessionStorage.clear();localStorage.clear();vi.useRealTimers();vi.unstubAllGlobals();vi.restoreAllMocks()})

describe('Sic Bo table',()=>{
  it('shows one title, a persistent dice tray and all 52 selectable targets',async()=>{
    render(<SicBoGame gateway={fakeGateway()}/>);await ready()
    expect(screen.getAllByRole('heading',{name:'Sic Bo'})).toHaveLength(1)
    expect(screen.getByRole('img',{name:'Dice tray'})).toBeTruthy()
    expect(screen.getAllByRole('button',{name:/^Add .+ bet$/})).toHaveLength(52)
    expect(screen.getByRole('region',{name:'Sic Bo betting table'})).toBeTruthy()
    expect(disabled('Roll dice')).toBe(true)
  })
  it('lets the host supply the title',async()=>{render(<SicBoGame gateway={fakeGateway()} showTitle={false}/>);await ready();expect(screen.queryByRole('heading',{name:'Sic Bo'})).toBeNull()})
  it('plays a quiet control cue for buttons and a distinct cue for a valid roll',async()=>{
    render(<SicBoGame gateway={fakeGateway()}/>);await ready()
    click('Rules');expect(playSicBoSound).toHaveBeenLastCalledWith('click')
    click('Close table details');add('Small');expect(playSicBoSound).toHaveBeenLastCalledWith('bet')
    click('Roll dice');expect(playSicBoSound).toHaveBeenLastCalledWith('roll')
    await screen.findByRole('button',{name:'New round'});expect(playSicBoSound).toHaveBeenLastCalledWith('win')
  })
  it('does not celebrate a net-losing round',async()=>{render(<SicBoGame gateway={fakeGateway()}/>);await ready();add('Big');click('Roll dice');await screen.findByRole('button',{name:'New round'});expect(playSicBoSound).not.toHaveBeenCalledWith('win')})
  it('flashes the net win in the center of the board',async()=>{render(<SicBoGame gateway={fakeGateway()}/>);await ready();add('Small');click('Roll dice');await screen.findByRole('button',{name:'New round'});expect(screen.getByLabelText('Won '+money(1)).textContent).toBe('+'+money(1))})
  it.each(betTargets.map(t=>[t.label,t.id] as const))('submits canonical %s selection',async(label,id)=>{
    const gateway=fakeGateway();render(<SicBoGame gateway={gateway}/>);await ready();add(label);click('Roll dice')
    await waitFor(()=>expect(gateway.createRound).toHaveBeenCalledTimes(1))
    expect(vi.mocked(gateway.createRound).mock.calls[0]![0]).toEqual([createTargetBet(id,1)])
  })
  it('preserves all ten kinds and ordered duplicate wagers',async()=>{
    const gateway=fakeGateway();render(<SicBoGame gateway={gateway}/>);await ready()
    ;['Small','Big','Odd','Even','Any Triple','Single 2','Double 2','Triple 2','Total 9','Combination 2 + 5','Small'].forEach(add);click('Roll dice')
    await waitFor(()=>expect(gateway.createRound).toHaveBeenCalledTimes(1));const bets=vi.mocked(gateway.createRound).mock.calls[0]![0]
    expect(bets.map(b=>b.kind)).toEqual(['small','big','odd','even','any-triple','single-number','specific-double','specific-triple','total','two-number-combination','small'])
    expect(bets[0]).toEqual(bets[10]);expect(bets).toHaveLength(11)
  })
  it('aggregates markers while retaining duplicate slip entries',async()=>{
    render(<SicBoGame gateway={fakeGateway()}/>);await ready();add('Small');stake('5');add('Small')
    expect(within(screen.getByRole('button',{name:'Add Small bet'})).getByLabelText(money(6)+' staked')).toBeTruthy()
    click('Slip 2');const panel=within(screen.getByRole('dialog'))
    expect(panel.getAllByText('Small')).toHaveLength(2);expect(panel.getByText(money(1))).toBeTruthy();expect(panel.getByText(money(5))).toBeTruthy()
  })
  it('removes an individual entry, undoes the last entry and clears the rest',async()=>{
    const gateway=fakeGateway();render(<SicBoGame gateway={gateway}/>);await ready();['Small','Total 9','Combination 2 + 5'].forEach(add)
    click('Slip 3');click('Remove bet 2');expect(within(screen.getByRole('dialog')).queryByText('Total 9')).toBeNull()
    click('Close table details');click('Undo');click('Slip 1');expect(within(screen.getByRole('dialog')).getByText('Small')).toBeTruthy()
    click('Clear');expect(within(screen.getByRole('dialog')).getByText('No bets placed.')).toBeTruthy();expect(gateway.createRound).not.toHaveBeenCalled()
  })
  it.each(['','0','-1','1e1','Infinity','1.001','1.','101','5x'])('locks additions for invalid exact stake %j',async raw=>{
    render(<SicBoGame gateway={fakeGateway()}/>);await ready();stake(raw)
    expect(screen.getByRole('textbox',{name:'Stake'}).getAttribute('aria-invalid')).toBe('true');expect(disabled('Add Small bet')).toBe(true);expect(disabled('Roll dice')).toBe(true)
  })
  it('uses the minimum as the stake-step anchor and offers legal chips',async()=>{
    const gateway=fakeGateway({getStatus:vi.fn().mockResolvedValue({...status,minimumStake:1.25,maximumStakePerBet:5.5,stakeIncrement:0.5})})
    render(<SicBoGame gateway={gateway}/>);await ready()
    expect((screen.getByRole('textbox',{name:'Stake'}) as HTMLInputElement).value).toBe('1.25')
    expect(screen.getByRole('button',{name:'Set stake '+money(1.25)})).toBeTruthy();expect(screen.getByRole('button',{name:'Set stake '+money(5.25)})).toBeTruthy()
    expect(screen.queryByRole('button',{name:'Set stake '+money(5)})).toBeNull();stake('1.5');expect(disabled('Add Small bet')).toBe(true)
    stake('1.75');add('Small');click('Roll dice');await waitFor(()=>expect(gateway.createRound).toHaveBeenCalled())
    expect(vi.mocked(gateway.createRound).mock.calls[0]![0]).toEqual([{...small,stake:1.75}])
  })
  it('sets a chip without placing a bet',async()=>{const gateway=fakeGateway();render(<SicBoGame gateway={gateway}/>);await ready();click('Set stake '+money(25));expect((screen.getByRole('textbox',{name:'Stake'}) as HTMLInputElement).value).toBe('25');expect(screen.getByRole('button',{name:'Slip 0'})).toBeTruthy();expect(gateway.createRound).not.toHaveBeenCalled()})
  it('enforces bet count synchronously during same-tick additions',async()=>{
    render(<SicBoGame gateway={fakeGateway({getStatus:vi.fn().mockResolvedValue({...status,maximumBetsPerRound:1})})}/>);await ready()
    const button=screen.getByRole('button',{name:'Add Small bet'});act(()=>{button.click();button.click()})
    expect(screen.getByRole('button',{name:'Slip 1'})).toBeTruthy();expect(disabled('Add Big bet')).toBe(true);expect(disabled('Roll dice')).toBe(false)
  })
  it('prevents overspending and permits a slip equal to balance',async()=>{
    render(<SicBoGame gateway={fakeGateway({getStatus:vi.fn().mockResolvedValue({...status,balance:5})})}/>);await ready();stake('5');add('Small')
    expect(disabled('Add Big bet')).toBe(true);expect(disabled('Roll dice')).toBe(false);click('Undo');expect(disabled('Add Big bet')).toBe(false)
  })
  it('shows actual payouts and limits in Rules, closes with Escape and restores focus',async()=>{
    const user=userEvent.setup();render(<SicBoGame gateway={fakeGateway()}/>);await ready();await user.click(screen.getByRole('button',{name:'Rules'}))
    const panel=within(screen.getByRole('dialog'));expect(panel.getByRole('heading',{name:'Rules & payouts'})).toBeTruthy()
    expect(panel.getByText(/Small, Big, Odd and Even lose on triples/)).toBeTruthy();expect(panel.getByText('11.5:1')).toBeTruthy();expect(panel.getByText('195:1')).toBeTruthy()
    expect(panel.getByText('Profit odds. A winning return includes the stake.')).toBeTruthy();expect(panel.getByText('Min '+money(1)+' · Max '+money(100)+' · Step '+money(1)+' · 20 bets.')).toBeTruthy()
    await user.keyboard('{Escape}');expect((screen.getByRole('dialog',{hidden:true}) as HTMLDialogElement).open).toBe(false);expect(document.activeElement).toBe(screen.getByRole('button',{name:'Rules'}))
  })
  it('keeps the table mounted and shows authoritative dice and settlement totals',async()=>{
    const callback=vi.fn();render(<SicBoGame gateway={fakeGateway()} onBalanceChange={callback}/>);await ready();const board=screen.getByRole('region',{name:'Sic Bo betting table'})
    ;['Small','Total 9','Combination 2 + 5'].forEach(add);click('Roll dice');await screen.findByRole('button',{name:'New round'})
    expect(screen.getByRole('region',{name:'Sic Bo betting table'})).toBe(board);expect(screen.getByRole('img',{name:'Dice 2, 2, 5'})).toBeTruthy()
    expect(screen.getByText('+'+money(14)+' net')).toBeTruthy();expect(screen.getByText(money(17))).toBeTruthy();expect(screen.getByText(money(114))).toBeTruthy()
    expect(callback).toHaveBeenCalledExactlyOnceWith(114);expect(disabled('Add Small bet')).toBe(true)
  })
  it('settles reduced-motion dice immediately and records once',async()=>{
    const callback=vi.fn();render(<SicBoGame playerId="history-player" gateway={fakeGateway()} onBalanceChange={callback}/>);await ready();add('Small');click('Roll dice');await screen.findByRole('button',{name:'New round'})
    expect(callback).toHaveBeenCalledExactlyOnceWith(101);expect(JSON.parse(localStorage.getItem(historyStorageKey('history-player'))!).rounds).toHaveLength(1)
    click('History');expect(within(screen.getByRole('dialog')).getByText('Bet '+money(1)+' · Return '+money(2))).toBeTruthy()
  })
  it('reveals normal-motion dice at180/340/520ms without early result, history or next actions',async()=>{
    motion(false);const callback=vi.fn();render(<SicBoGame playerId="motion-player" gateway={fakeGateway()} onBalanceChange={callback}/>);await ready();vi.useFakeTimers();add('Small');click('Roll dice');await act(async()=>{await Promise.resolve()})
    const tray=screen.getByRole('img',{name:'Dice revealing'});expect(tray.querySelectorAll('i')).toHaveLength(0)
    expect(screen.queryByRole('img',{name:'Dice 2, 2, 5'})).toBeNull();expect(screen.queryByText(money(2))).toBeNull();expect(screen.queryByRole('button',{name:'New round'})).toBeNull();expect(screen.queryByRole('button',{name:'Repeat bets'})).toBeNull()
    expect(localStorage.getItem(historyStorageKey('motion-player'))).toBeNull();expect(callback).not.toHaveBeenCalled()
    await act(async()=>{vi.advanceTimersByTime(180)});expect(tray.querySelectorAll('i')).toHaveLength(2)
    await act(async()=>{vi.advanceTimersByTime(160)});expect(tray.querySelectorAll('i')).toHaveLength(4)
    await act(async()=>{vi.advanceTimersByTime(179)});expect(callback).not.toHaveBeenCalled();expect(screen.queryByRole('button',{name:'New round'})).toBeNull()
    await act(async()=>{vi.advanceTimersByTime(1)});expect(tray.querySelectorAll('i')).toHaveLength(9);expect(screen.getByRole('img',{name:'Dice 2, 2, 5'})).toBeTruthy();expect(screen.getByRole('button',{name:'New round'})).toBeTruthy()
    expect(callback).toHaveBeenCalledExactlyOnceWith(101);expect(JSON.parse(localStorage.getItem(historyStorageKey('motion-player'))!).rounds).toHaveLength(1)
  })
  it('gates same-tick double rolls and every edit synchronously',async()=>{
    const pending=deferred<SicBoRound>(),gateway=fakeGateway({createRound:vi.fn().mockReturnValue(pending.promise)});render(<SicBoGame gateway={gateway}/>);await ready();add('Small')
    const roll=screen.getByRole('button',{name:'Roll dice'});act(()=>{roll.click();roll.click();screen.getByRole('button',{name:'Add Big bet'}).click()})
    expect(gateway.createRound).toHaveBeenCalledTimes(1);expect(screen.getByRole('button',{name:'Slip 1'})).toBeTruthy();expect(disabled('Roll dice')).toBe(true);expect(disabled('Undo')).toBe(true)
    expect(screen.getByRole('textbox',{name:'Stake'}).hasAttribute('disabled')).toBe(true);await act(async()=>{pending.resolve(roundFor([small]))});expect(screen.getByRole('button',{name:'New round'})).toBeTruthy()
  })
  it('prepares repeat bets without posting until an explicit roll',async()=>{
    const gateway=fakeGateway();render(<SicBoGame gateway={gateway}/>);await ready();add('Small');click('Roll dice');clickAfter(await screen.findByRole('button',{name:'Repeat bets'}))
    expect(screen.getByRole('button',{name:'Slip 1'})).toBeTruthy();expect(gateway.createRound).toHaveBeenCalledTimes(1);expect(screen.getByRole('img',{name:'Dice 2, 2, 5'})).toBeTruthy();expect(screen.queryByText('+'+money(1)+' net')).toBeNull()
    click('Roll dice');await screen.findByRole('button',{name:'New round'});expect(gateway.createRound).toHaveBeenCalledTimes(2);expect(gateway.getStatus).toHaveBeenCalledTimes(1)
  })
  it('prepares an empty slip while retaining last dice and settled balance',async()=>{
    const gateway=fakeGateway();render(<SicBoGame gateway={gateway}/>);await ready();add('Small');click('Roll dice');clickAfter(await screen.findByRole('button',{name:'New round'}))
    expect(screen.getByRole('button',{name:'Slip 0'})).toBeTruthy();expect(screen.getByRole('img',{name:'Dice 2, 2, 5'})).toBeTruthy();expect(screen.getByText(money(101))).toBeTruthy();expect(gateway.getStatus).toHaveBeenCalledTimes(1);expect(gateway.createRound).toHaveBeenCalledTimes(1)
  })
  it('disables repeat when the final balance cannot cover the old slip',async()=>{
    const gateway=fakeGateway({createRound:vi.fn(async bets=>roundFor(bets,{balance:0}))});render(<SicBoGame gateway={gateway}/>);await ready();add('Big');click('Roll dice');await screen.findByRole('button',{name:'Repeat bets'})
    expect(disabled('Repeat bets')).toBe(true);expect(disabled('New round')).toBe(false)
  })
  it('renders a server triple with quick-bet loss and the actual triple, double and total payouts',async()=>{
    const ids=['small','any-triple','specific-triple-2','single-number-2','specific-double-2','total-6'],bets=ids.map(id=>createTargetBet(id,1)!)
    const odds=[0,32,195,12,11.5,19],settlements=bets.map((bet,index)=>({...bet,betIndex:index,won:odds[index]!>0,profitOdds:odds[index]!,profit:odds[index]||-1,totalReturn:odds[index]?odds[index]!+1:0}))
    const triple:SicBoRound={roundId:defaultId,phase:'settled',dice:[2,2,2],total:6,isTriple:true,totalStaked:6,totalReturn:274.5,profit:268.5,balance:368.5,settlements}
    render(<SicBoGame gateway={fakeGateway({createRound:vi.fn().mockResolvedValue(triple)})}/>);await ready()
    ;['Small','Any Triple','Triple 2','Single 2','Double 2','Total 6'].forEach(add);click('Roll dice');await screen.findByRole('button',{name:'New round'})
    expect(screen.getByRole('img',{name:'Dice 2, 2, 2'})).toBeTruthy();expect(screen.getByText('Triple')).toBeTruthy();expect(screen.getByText(money(274.5))).toBeTruthy()
    expect(within(screen.getByRole('button',{name:'Add Small bet'})).getByLabelText('Losing bet')).toBeTruthy()
    expect(within(screen.getByRole('button',{name:'Add Any Triple bet'})).getByLabelText('Winning bet')).toBeTruthy()
    click('Slip 6');expect(within(screen.getByRole('dialog')).getByText('Won · '+money(196)+' return')).toBeTruthy();expect(within(screen.getByRole('dialog')).getByText('Won · '+money(12.5)+' return')).toBeTruthy()
  })
})

describe('Sic Bo restoration and request ownership',()=>{
  it.each(['account','practice'] as const)('restores%s result with owner/key-derived GET alone',async mode=>{
    const saved=savePending(mode),gateway=fakeGateway({getRound:vi.fn().mockResolvedValue(roundFor(saved.bets,{roundId:saved.roundId}))},mode)
    render(<SicBoGame roundOwnerId={owner} recoveryMode={mode} gateway={gateway}/>);await screen.findByRole('button',{name:'New round'})
    expect(gateway.getRound).toHaveBeenCalledExactlyOnceWith(saved.roundId,expect.any(AbortSignal));expect(gateway.createRound).not.toHaveBeenCalled();expect(sessionStorage.getItem(pendingStorageKey(saved.scope))).toBeNull();expect(screen.getByRole('button',{name:'Slip 3'})).toBeTruthy()
  })
  it('does not read an arbitrary forged64hex ID',async()=>{
    savePending('account',{roundId:'f'.repeat(64)});const gateway=fakeGateway({},'account');render(<SicBoGame roundOwnerId={owner} recoveryMode="account" gateway={gateway}/>);await screen.findByRole('alert')
    expect(gateway.getRound).not.toHaveBeenCalled();expect(gateway.createRound).not.toHaveBeenCalled();expect(disabled('Add Small bet')).toBe(true)
  })
  it('requires explicit host mode and owner to restore a saved result',async()=>{
    const saved=savePending('account'),gateway=fakeGateway();render(<SicBoGame playerId={saved.scope} gateway={gateway}/>);await screen.findByRole('alert')
    expect(gateway.getRound).not.toHaveBeenCalled();expect(gateway.createRound).not.toHaveBeenCalled();expect(disabled('Roll dice')).toBe(true)
  })
  it('does not use storage from another scope',async()=>{
    const saved=savePending('account');sessionStorage.setItem(pendingStorageKey(saved.scope),JSON.stringify({...saved,scope:'someone-else'}));const gateway=fakeGateway({},'account')
    render(<SicBoGame roundOwnerId={owner} recoveryMode="account" gateway={gateway}/>);await ready();expect(gateway.getRound).not.toHaveBeenCalled();expect(gateway.createRound).not.toHaveBeenCalled();expect(screen.getByRole('button',{name:'Slip 0'})).toBeTruthy()
  })
  it('keeps a mismatched recovered ordered slip locked',async()=>{
    const saved=savePending('account'),gateway=fakeGateway({getRound:vi.fn().mockResolvedValue(roundFor([...saved.bets].reverse(),{roundId:saved.roundId}))},'account')
    render(<SicBoGame roundOwnerId={owner} recoveryMode="account" gateway={gateway}/>);await screen.findByRole('alert');expect(disabled('Add Small bet')).toBe(true);expect(gateway.createRound).not.toHaveBeenCalled();expect(sessionStorage.getItem(pendingStorageKey(saved.scope))).not.toBeNull()
  })
  it('retries a failed GET without replaying POST',async()=>{
    const saved=savePending('account'),gateway=fakeGateway({getRound:vi.fn().mockRejectedValueOnce(new Error('Offline')).mockResolvedValueOnce(roundFor(saved.bets,{roundId:saved.roundId}))},'account')
    render(<SicBoGame roundOwnerId={owner} recoveryMode="account" gateway={gateway}/>);clickAfter(await screen.findByRole('button',{name:'Retry restoration'}));await screen.findByRole('button',{name:'New round'})
    expect(gateway.getRound).toHaveBeenCalledTimes(2);expect(gateway.createRound).not.toHaveBeenCalled()
  })
  it('keeps a missing practice result locked and retries only GET',async()=>{
    savePending('practice');const gateway=fakeGateway({getRound:vi.fn().mockRejectedValue(missing())},'practice');render(<SicBoGame roundOwnerId={owner} recoveryMode="practice" gateway={gateway}/>)
    clickAfter(await screen.findByRole('button',{name:'Retry restoration'}));await waitFor(()=>expect(gateway.getRound).toHaveBeenCalledTimes(2));expect(gateway.createRound).not.toHaveBeenCalled();expect(screen.queryByRole('button',{name:'Retry roll'})).toBeNull();expect(disabled('Add Small bet')).toBe(true)
  })
  it('explicitly retries paid404 with the exact key and ordered duplicate slip despite lower balance',async()=>{
    const saved=savePending('account',{bets:[small,totalNine,small]}),gateway=fakeGateway({getRound:vi.fn().mockRejectedValue(missing()),getStatus:vi.fn().mockResolvedValue({...status,balance:0})},'account')
    render(<SicBoGame roundOwnerId={owner} recoveryMode="account" gateway={gateway}/>);const retry=await screen.findByRole('button',{name:'Retry roll'});expect(gateway.createRound).not.toHaveBeenCalled();await waitFor(()=>expect(retry.hasAttribute('disabled')).toBe(false));clickAfter(retry);await screen.findByRole('button',{name:'New round'})
    expect(gateway.getRound).toHaveBeenCalledTimes(2);expect(gateway.createRound).toHaveBeenCalledExactlyOnceWith(saved.bets,expect.objectContaining({idempotencyKey:savedKey,signal:expect.any(AbortSignal)}))
  })
  it('persists a verified paid ID before POST and retries ambiguity with the unchanged key and slip',async()=>{
    let storedAtPost:unknown;const createRound=vi.fn().mockImplementationOnce(async()=>{storedAtPost=JSON.parse(sessionStorage.getItem(pendingStorageKey(owner+':account'))!);throw new Error('Response lost')}).mockImplementationOnce(async(bets,options)=>roundFor(bets,{roundId:hashId(options.idempotencyKey,'account')}))
    const gateway=fakeGateway({createRound,getRound:vi.fn().mockRejectedValue(missing())},'account');render(<SicBoGame roundOwnerId={owner} recoveryMode="account" gateway={gateway}/>);await ready();add('Small');add('Total 9');click('Roll dice');const retry=await screen.findByRole('button',{name:'Retry roll'})
    const key=createRound.mock.calls[0]![1].idempotencyKey;expect(storedAtPost).toEqual({scope:owner+':account',idempotencyKey:key,bets:[small,totalNine],roundId:hashId(key,'account')});expect(disabled('Add Big bet')).toBe(true)
    clickAfter(retry);await screen.findByRole('button',{name:'New round'});expect(createRound.mock.calls[1]![0]).toEqual(createRound.mock.calls[0]![0]);expect(createRound.mock.calls[1]![1].idempotencyKey).toBe(key);expect(sessionStorage.getItem(pendingStorageKey(owner+':account'))).toBeNull()
  })
  it('uses GET alone after an ambiguous practice POST including404',async()=>{
    const gateway=fakeGateway({createRound:vi.fn().mockRejectedValue(new Error('Response lost')),getRound:vi.fn().mockRejectedValue(missing())},'practice')
    render(<SicBoGame roundOwnerId={owner} recoveryMode="practice" gateway={gateway}/>);await ready();add('Small');click('Roll dice');clickAfter(await screen.findByRole('button',{name:'Retry restoration'}));await waitFor(()=>expect(gateway.getRound).toHaveBeenCalledTimes(1));expect(gateway.createRound).toHaveBeenCalledTimes(1);expect(screen.queryByRole('button',{name:'Retry roll'})).toBeNull()
  })
  it('uses the confirmed GET result on a paid retry without issuing a second POST',async()=>{
    const id=hashId('sic-bo-'+uuid.replaceAll('-',''),'account'),gateway=fakeGateway({createRound:vi.fn().mockRejectedValue(new Error('Response lost')),getRound:vi.fn().mockResolvedValue(roundFor([small],{roundId:id}))},'account')
    render(<SicBoGame roundOwnerId={owner} recoveryMode="account" gateway={gateway}/>);await ready();add('Small');click('Roll dice');clickAfter(await screen.findByRole('button',{name:'Retry roll'}));await screen.findByRole('button',{name:'New round'})
    expect(gateway.createRound).toHaveBeenCalledTimes(1);expect(gateway.getRound).toHaveBeenCalledExactlyOnceWith(id,expect.any(AbortSignal));expect(screen.getByText(/^Balance/).textContent).toBe('Balance '+money(101))
  })
  it('retains saved paid identity until the normal-motion reveal finishes',async()=>{
    motion(false);const key='sic-bo-'+uuid.replaceAll('-',''),id=hashId(key,'account'),buffer=Uint8Array.from(id.match(/../g)!,byte=>parseInt(byte,16)).buffer
    vi.spyOn(crypto.subtle,'digest').mockResolvedValueOnce(buffer)
    const gateway=fakeGateway({},'account');render(<SicBoGame roundOwnerId={owner} recoveryMode="account" gateway={gateway}/>);await ready();vi.useFakeTimers();add('Small');click('Roll dice')
    await act(async()=>{await Promise.resolve();await Promise.resolve()})
    expect(screen.getByRole('img',{name:'Dice revealing'})).toBeTruthy();expect(JSON.parse(sessionStorage.getItem(pendingStorageKey(owner+':account'))!).roundId).toBe(id)
    await act(async()=>{vi.advanceTimersByTime(519)});expect(sessionStorage.getItem(pendingStorageKey(owner+':account'))).not.toBeNull();expect(screen.queryByRole('button',{name:'New round'})).toBeNull()
    await act(async()=>{vi.advanceTimersByTime(1)});expect(sessionStorage.getItem(pendingStorageKey(owner+':account'))).toBeNull();expect(screen.getByRole('button',{name:'New round'})).toBeTruthy()
  })
  it('publishes a delayed POST settlement to the latest callback without repeating POST',async()=>{
    const pending=deferred<SicBoRound>(),gateway=fakeGateway({createRound:vi.fn().mockReturnValue(pending.promise)}),first=vi.fn(),next=vi.fn()
    const view=render(<SicBoGame gateway={gateway} onBalanceChange={first}/>);await ready();add('Small');click('Roll dice');view.rerender(<SicBoGame gateway={gateway} onBalanceChange={next}/>);await act(async()=>{pending.resolve(roundFor([small]))})
    expect(gateway.createRound).toHaveBeenCalledTimes(1);expect(first).not.toHaveBeenCalled();expect(next).toHaveBeenCalledExactlyOnceWith(101)
  })
  it('locks an ambiguous sample POST without claiming durable replay',async()=>{
    const gateway=fakeGateway({createRound:vi.fn().mockRejectedValue(new Error('Response lost'))});render(<SicBoGame gateway={gateway}/>);await ready();add('Small');click('Roll dice');await screen.findByRole('alert')
    expect(screen.queryByRole('button',{name:'Retry roll'})).toBeNull();expect(disabled('Retry restoration')).toBe(true);expect(gateway.getRound).not.toHaveBeenCalled();expect(sessionStorage.getItem(pendingStorageKey('guest'))).toBeNull()
  })
  it.each([new SicBoGatewayError('Invalid stake','sic-bo-invalid-request',400),new SicBoGatewayError('Insufficient','insufficient-slot-credits',409)])('allows editing after fresh definitive decline%s',async decline=>{
    const gateway=fakeGateway({createRound:vi.fn().mockRejectedValue(decline)},'account');render(<SicBoGame roundOwnerId={owner} recoveryMode="account" gateway={gateway}/>);await ready();add('Small');click('Roll dice');await screen.findByRole('alert')
    expect(disabled('Undo')).toBe(false);expect(disabled('Add Big bet')).toBe(false);expect(sessionStorage.getItem(pendingStorageKey(owner+':account'))).toBeNull();expect(gateway.getStatus).toHaveBeenCalledTimes(2);click('Undo');expect(screen.getByRole('button',{name:'Slip 0'})).toBeTruthy();expect(gateway.createRound).toHaveBeenCalledTimes(1)
  })
  it('never discards a prior ambiguous slip merely because its retry is declined',async()=>{
    savePending('account');const gateway=fakeGateway({getRound:vi.fn().mockRejectedValue(missing()),createRound:vi.fn().mockRejectedValue(new SicBoGatewayError('Insufficient','insufficient-slot-credits',409))},'account')
    render(<SicBoGame roundOwnerId={owner} recoveryMode="account" gateway={gateway}/>);clickAfter(await screen.findByRole('button',{name:'Retry roll'}));await waitFor(()=>expect(gateway.createRound).toHaveBeenCalledTimes(1));await screen.findByRole('alert');expect(disabled('Undo')).toBe(true);expect(sessionStorage.getItem(pendingStorageKey(owner+':account'))).not.toBeNull()
  })
  it('uses the latest callback without replaying a restoration or POST',async()=>{
    const saved=savePending('account'),pending=deferred<SicBoRound>(),gateway=fakeGateway({getRound:vi.fn().mockReturnValue(pending.promise)},'account'),first=vi.fn(),next=vi.fn()
    const view=render(<SicBoGame roundOwnerId={owner} recoveryMode="account" gateway={gateway} onBalanceChange={first}/>);await waitFor(()=>expect(gateway.getRound).toHaveBeenCalledTimes(1))
    view.rerender(<SicBoGame roundOwnerId={owner} recoveryMode="account" gateway={gateway} onBalanceChange={next}/>);await act(async()=>{pending.resolve(roundFor(saved.bets,{roundId:saved.roundId}))})
    expect(gateway.getRound).toHaveBeenCalledTimes(1);expect(gateway.createRound).not.toHaveBeenCalled();expect(first).not.toHaveBeenCalled();expect(next).toHaveBeenCalledExactlyOnceWith(114)
  })
  it('ignores a late status balance after restoring a known result',async()=>{
    const saved=savePending('account'),pending=deferred<SicBoStatus>(),gateway=fakeGateway({getStatus:vi.fn().mockReturnValue(pending.promise),getRound:vi.fn().mockResolvedValue(roundFor(saved.bets,{roundId:saved.roundId,balance:114}))},'account')
    render(<SicBoGame roundOwnerId={owner} recoveryMode="account" gateway={gateway}/>);await screen.findByRole('button',{name:'New round'});await act(async()=>{pending.resolve({...status,balance:3})});expect(screen.getByText(/^Balance/).textContent).toBe('Balance '+money(114))
  })
  it.each(['account','gateway','mode','owner'] as const)('aborts stale requests when%s ownership changes',async change=>{
    const pending=deferred<SicBoRound>(),oldCallback=vi.fn(),nextCallback=vi.fn(),gateway=fakeGateway({createRound:vi.fn().mockReturnValue(pending.promise)},'account'),other=fakeGateway({getRound:vi.fn().mockReturnValue(deferred<SicBoRound>().promise)},'account')
    const view=render(<SicBoGame playerId="paid-player" roundOwnerId={owner} recoveryMode="account" gateway={gateway} onBalanceChange={oldCallback}/>);await ready();add('Small');click('Roll dice');await waitFor(()=>expect(gateway.createRound).toHaveBeenCalledTimes(1));const signal=vi.mocked(gateway.createRound).mock.calls[0]![1]!.signal!
    view.rerender(<SicBoGame playerId="paid-player" roundOwnerId={change==='account'||change==='owner'?'next-owner':owner} recoveryMode={change==='mode'?'practice':'account'} gateway={change==='gateway'?other:gateway} onBalanceChange={nextCallback}/>);expect(signal.aborted).toBe(true)
    await act(async()=>{pending.resolve(roundFor([small]))});expect(screen.queryByRole('button',{name:'New round'})).toBeNull();expect(oldCallback).not.toHaveBeenCalled();expect(nextCallback).not.toHaveBeenCalled()
  })
  it('aborts a removed table and ignores transport that resolves after abort',async()=>{
    const pending=deferred<SicBoRound>(),callback=vi.fn(),gateway=fakeGateway({createRound:vi.fn().mockReturnValue(pending.promise)})
    const view=render(<SicBoGame gateway={gateway} onBalanceChange={callback}/>);await ready();add('Small');click('Roll dice');const signal=vi.mocked(gateway.createRound).mock.calls[0]![1]!.signal!;view.unmount();expect(signal.aborted).toBe(true)
    await act(async()=>{pending.resolve(roundFor([small]))});expect(callback).not.toHaveBeenCalled();expect(localStorage.getItem(historyStorageKey('guest'))).toBeNull()
  })
})

describe('Sic Bo availability',()=>{
  it('is locked while connecting',async()=>{const pending=deferred<SicBoStatus>(),gateway=fakeGateway({getStatus:vi.fn().mockReturnValue(pending.promise)});render(<SicBoGame gateway={gateway}/>);expect(disabled('Add Small bet')).toBe(true);expect(disabled('Roll dice')).toBe(true);expect(gateway.createRound).not.toHaveBeenCalled();await act(async()=>{pending.resolve(status)});expect(disabled('Add Small bet')).toBe(false)})
  it('retries a failed connection without a refresh',async()=>{const getStatus=vi.fn().mockRejectedValueOnce(new Error('Offline')).mockResolvedValueOnce(status);render(<SicBoGame gateway={fakeGateway({getStatus})}/>);clickAfter(await screen.findByRole('button',{name:'Retry connection'}));await ready();expect(getStatus).toHaveBeenCalledTimes(2)})
  it.each([vi.fn().mockResolvedValue({...status,available:false}),vi.fn().mockRejectedValue(new SicBoGatewayError('Table disabled','sic-bo-disabled',503))])('cannot wager at an unavailable table',async getStatus=>{const gateway=fakeGateway({getStatus});render(<SicBoGame gateway={gateway}/>);await screen.findByRole('alert');expect(disabled('Add Small bet')).toBe(true);expect(disabled('Roll dice')).toBe(true);expect(gateway.createRound).not.toHaveBeenCalled()})
  it('locks edits throughout restoration despite status succeeding',async()=>{savePending('account');const gateway=fakeGateway({getRound:vi.fn().mockReturnValue(deferred<SicBoRound>().promise)},'account');render(<SicBoGame roundOwnerId={owner} recoveryMode="account" gateway={gateway}/>);await waitFor(()=>expect(gateway.getRound).toHaveBeenCalled());expect(screen.getByRole('textbox',{name:'Stake'}).hasAttribute('disabled')).toBe(true);expect(disabled('Add Small bet')).toBe(true);expect(disabled('Undo')).toBe(true);expect(gateway.createRound).not.toHaveBeenCalled()})
})

function click(name:string){fireEvent.click(screen.getByRole('button',{name}))}
function clickAfter(node:HTMLElement){fireEvent.click(node)}
function add(label:string){click('Add '+label+' bet')}
function stake(value:string){fireEvent.change(screen.getByRole('textbox',{name:'Stake'}),{target:{value}})}
function disabled(name:string){return screen.getByRole('button',{name}).hasAttribute('disabled')}
function money(value:number){return 'R'+value.toLocaleString('en-ZA',{minimumFractionDigits:2,maximumFractionDigits:2})}
async function ready(){await waitFor(()=>expect(disabled('Add Small bet')).toBe(false))}
function motion(reduce:boolean){vi.stubGlobal('matchMedia',(query:string)=>({matches:reduce&&query.includes('prefers-reduced-motion'),media:query,onchange:null,addListener:vi.fn(),removeListener:vi.fn(),addEventListener:vi.fn(),removeEventListener:vi.fn(),dispatchEvent:vi.fn()}))}
function hashId(key:string,mode:Mode){return createHash('sha256').update(mode==='practice'?'practice\nsic-bo\n'+owner+'\n'+key:owner+'\n'+key).digest('hex')}
function savePending(mode:Mode,overrides:Partial<{bets:readonly SicBoBetRequest[];roundId:string}>={}){const saved={scope:owner+':'+mode,idempotencyKey:savedKey,bets:[small,totalNine,combination],roundId:hashId(savedKey,mode),...overrides};sessionStorage.setItem(pendingStorageKey(saved.scope),JSON.stringify(saved));return saved}
function deferred<T>(){let resolve!:(value:T)=>void;const promise=new Promise<T>(complete=>{resolve=complete});return{promise,resolve}}
function missing(){return new SicBoGatewayError('Missing','sic-bo-round-not-found',404)}
function fakeGateway(overrides:Partial<SicBoGateway>={},mode?:Mode):SicBoGateway{return{getStatus:vi.fn().mockResolvedValue(status),getRound:vi.fn().mockResolvedValue(roundFor([small])),createRound:vi.fn(async(bets,options)=>roundFor(bets,{roundId:mode?hashId(options!.idempotencyKey!,mode):defaultId})),...overrides}}
/** Fixed [2,2,5] server fixture outcomes; total9 pays the actual7:1. */
function roundFor(bets:readonly SicBoBetRequest[],options:{roundId?:string;balance?:number}={}):SicBoRound{
  const settlements=bets.map((bet,betIndex)=>{const odds=bet.kind==='small'||bet.kind==='odd'?1:bet.kind==='single-number'?bet.face===2?2:bet.face===5?1:0:bet.kind==='specific-double'&&bet.face===2?11.5:bet.kind==='total'&&bet.total===9?7:bet.kind==='two-number-combination'&&bet.firstFace===2&&bet.secondFace===5?6:0;return{...bet,betIndex,won:odds>0,profitOdds:odds,profit:odds?bet.stake*odds:-bet.stake,totalReturn:odds?bet.stake*(odds+1):0}})
  const totalStaked=bets.reduce((sum,b)=>sum+b.stake,0),totalReturn=settlements.reduce((sum,b)=>sum+b.totalReturn,0)
  return{roundId:options.roundId??defaultId,phase:'settled',dice:[2,2,5],total:9,isTriple:false,totalStaked,totalReturn,profit:totalReturn-totalStaked,balance:options.balance??100-totalStaked+totalReturn,settlements}
}
