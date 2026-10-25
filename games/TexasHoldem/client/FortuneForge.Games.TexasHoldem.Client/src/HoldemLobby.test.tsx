// @vitest-environment jsdom
import { cleanup, fireEvent, render, screen } from '@testing-library/react'
import { useState } from 'react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { HoldemLobby, type HoldemLobbyProps } from './HoldemLobby'
import { HoldemQueue } from './HoldemQueue'
import { testRule } from './testFixtures'

afterEach(cleanup)
const highRule = { ...testRule, id: 'high', name: 'High stakes', smallBlindCredits: 2.5, bigBlindCredits: 5, maximumTableStackCredits: 500 }

type HarnessProps = Omit<HoldemLobbyProps, 'buyInAmount' | 'onBuyInAmountChange'> & { initialAmount?: string }
function LobbyHarness({ initialAmount, ...props }: HarnessProps) {
  const selected = props.rules.find(rule => rule.id === props.selectedRuleId) ?? props.rules[0]
  const maximum = Math.floor(Math.min(props.balanceCredits, selected?.maximumTableStackCredits ?? 0) * 100 + .000001) / 100
  const [amount, setAmount] = useState(initialAmount ?? (maximum >= (selected?.minimumBuyInCredits ?? selected?.bigBlindCredits ?? 1) ? maximum.toFixed(2) : ''))
  return <HoldemLobby {...props} buyInAmount={amount} onBuyInAmountChange={setAmount}/>
}

describe('HoldemLobby', () => {
  it('offers all three production stake levels with their independent buy-in limits', () => {
    const rules = [
      { ...testRule, id: 'friendly', name: 'Friendly', smallBlindCredits: .25, bigBlindCredits: .5, maximumTableStackCredits: 25 },
      testRule,
      { ...testRule, id: 'bold', name: 'Bold', smallBlindCredits: 2, bigBlindCredits: 5, maximumTableStackCredits: 500 },
    ]
    const onSelectRule = vi.fn(), onJoin = vi.fn()
    const props = { rules, selectedRuleId: 'standard', balanceCredits: 1000, busy: false, onSelectRule, onJoin }
    const { rerender } = render(<LobbyHarness {...props}/>)
    expect(screen.getAllByRole('radio')).toHaveLength(3)
    expect(screen.getByRole('radio', { name: /^Friendly/ })).toBeTruthy()
    expect(screen.getByRole('radio', { name: /^Standard/ })).toBeTruthy()
    fireEvent.click(screen.getByRole('radio', { name: /^Bold/ }))
    expect(onSelectRule).toHaveBeenCalledWith('bold')
    rerender(<LobbyHarness {...props} selectedRuleId="bold"/>)
    fireEvent.click(screen.getByRole('button', { name: 'Join Bold · R500.00' }))
    expect(onJoin).toHaveBeenCalledWith(rules[2], 500)
  })

  it('uses decimal credit amounts and returns the selected authoritative rule on join', () => {
    const onJoin = vi.fn(), onSelectRule = vi.fn()
    render(<LobbyHarness rules={[testRule, highRule]} selectedRuleId="high" balanceCredits={25} busy={false} onSelectRule={onSelectRule} onJoin={onJoin} />)
    expect(screen.getAllByText('R25.00').length).toBeGreaterThan(0)
    expect(screen.getByText('R2.50 / R5.00')).toBeTruthy()
    expect(screen.getByRole('radio', { name: /High stakes/ }).getAttribute('aria-checked')).toBe('true')
    fireEvent.click(screen.getByRole('radio', { name: /Standard/ }))
    expect(onSelectRule).toHaveBeenCalledWith('standard')
    fireEvent.click(screen.getByRole('button', { name: 'Join High stakes · R25.00' }))
    expect(onJoin).toHaveBeenCalledWith(highRule, 25)
    expect(screen.getByText(/Moved from your account to table chips/)).toBeTruthy()
  })

  it('requires one big blind in real credits, not table cents, and guards busy selection/join', () => {
    const onJoin = vi.fn(), onSelectRule = vi.fn()
    const { rerender } = render(<LobbyHarness rules={[testRule]} selectedRuleId="standard" balanceCredits={.99} busy={false} onSelectRule={onSelectRule} onJoin={onJoin} />)
    expect((screen.getByRole('button', { name: 'Choose a buy-in' }) as HTMLButtonElement).disabled).toBe(true)
    rerender(<LobbyHarness rules={[testRule]} selectedRuleId="standard" balanceCredits={1} busy={false} onSelectRule={onSelectRule} onJoin={onJoin} />)
    expect((screen.getByRole('button', { name: 'Join Standard · R1.00' }) as HTMLButtonElement).disabled).toBe(false)
    rerender(<LobbyHarness rules={[testRule]} selectedRuleId="standard" balanceCredits={1} busy={true} onSelectRule={onSelectRule} onJoin={onJoin} />)
    fireEvent.click(screen.getByRole('radio'))
    fireEvent.click(screen.getByRole('button', { name: 'Finding a seat…' }))
    expect(onSelectRule).not.toHaveBeenCalled()
    expect(onJoin).not.toHaveBeenCalled()
  })

  it('handles missing rules without an undefined join or a fake table', () => {
    const onJoin = vi.fn()
    render(<LobbyHarness rules={[]} selectedRuleId="missing" balanceCredits={100} busy={false} onSelectRule={vi.fn()} onJoin={onJoin} />)
    const join = screen.getByRole('button', { name: 'No tables available' }) as HTMLButtonElement
    expect(join.disabled).toBe(true)
    fireEvent.click(join)
    expect(onJoin).not.toHaveBeenCalled()
  })

  it('falls back to the first real rule when a previous selection no longer exists', () => {
    const onJoin = vi.fn()
    render(<LobbyHarness rules={[testRule]} selectedRuleId="removed" balanceCredits={100} busy={false} onSelectRule={vi.fn()} onJoin={onJoin} />)
    fireEvent.click(screen.getByRole('button', { name: 'Join Standard · R100.00' }))
    expect(onJoin).toHaveBeenCalledWith(testRule, 100)
  })

  it('defaults to the smaller of the account balance and table buy-in cap', () => {
    render(<LobbyHarness rules={[testRule]} selectedRuleId="standard" balanceCredits={1000} busy={false} onSelectRule={vi.fn()} onJoin={vi.fn()} />)
    expect((screen.getByLabelText('Table chips to buy in') as HTMLInputElement).value).toBe('100.00')
    expect(screen.getByText('Account balance')).toBeTruthy()
    expect(screen.getByText('R1,000.00')).toBeTruthy()
    expect((screen.getByRole('slider', { name: 'Buy-in amount' }) as HTMLInputElement).max).toBe('10000')
  })

  it('sends selected whole-cent credits without converting to table cents too soon', () => {
    const onJoin = vi.fn()
    render(<LobbyHarness rules={[testRule]} selectedRuleId="standard" balanceCredits={100} busy={false} onSelectRule={vi.fn()} onJoin={onJoin} />)
    const input = screen.getByLabelText('Table chips to buy in')
    fireEvent.change(input, { target: { value: '12.35' } })
    fireEvent.blur(input)
    fireEvent.click(screen.getByRole('button', { name: 'Join Standard · R12.35' }))
    expect(onJoin).toHaveBeenCalledWith(testRule, 12.35)
  })

  it.each(['', '0', '-1', '.99', '100.01', '1.001', '1e2', 'NaN', 'abc'])('clears invalid amount %s on blur and never joins', amount => {
    const onJoin = vi.fn()
    render(<LobbyHarness rules={[testRule]} selectedRuleId="standard" balanceCredits={100} busy={false} onSelectRule={vi.fn()} onJoin={onJoin} />)
    const input = screen.getByLabelText('Table chips to buy in') as HTMLInputElement
    fireEvent.change(input, { target: { value: amount } })
    const join = screen.getByRole('button', { name: 'Choose a buy-in' }) as HTMLButtonElement
    expect(join.disabled).toBe(true)
    fireEvent.click(join)
    fireEvent.blur(input)
    expect(input.value).toBe('')
    expect(onJoin).not.toHaveBeenCalled()
  })

  it('provides exact bounded min, half and max presets plus a cents-based range', () => {
    const onJoin = vi.fn()
    render(<LobbyHarness rules={[testRule]} selectedRuleId="standard" balanceCredits={50.55} busy={false} onSelectRule={vi.fn()} onJoin={onJoin} />)
    fireEvent.click(screen.getByRole('button', { name: 'Min R1.00' }))
    expect(screen.getByRole('button', { name: 'Join Standard · R1.00' })).toBeTruthy()
    fireEvent.click(screen.getByRole('button', { name: 'Half R25.28' }))
    expect(screen.getByRole('button', { name: 'Join Standard · R25.28' })).toBeTruthy()
    fireEvent.click(screen.getByRole('button', { name: 'Max R50.55' }))
    expect(screen.getByRole('button', { name: 'Join Standard · R50.55' })).toBeTruthy()
    const slider = screen.getByRole('slider', { name: 'Buy-in amount' }) as HTMLInputElement
    expect(slider.min).toBe('100')
    expect(slider.max).toBe('5055')
    fireEvent.change(slider, { target: { value: '2743' } })
    fireEvent.click(screen.getByRole('button', { name: 'Join Standard · R27.43' }))
    expect(onJoin).toHaveBeenCalledWith(testRule, 27.43)
  })

  it('resets to the new rule maximum and clamps when the available balance decreases', () => {
    const props = { rules: [testRule, highRule], selectedRuleId: 'standard', balanceCredits: 1000, busy: false, onSelectRule: vi.fn(), onJoin: vi.fn() }
    const { rerender } = render(<LobbyHarness {...props}/>)
    fireEvent.change(screen.getByLabelText('Table chips to buy in'), { target: { value: '10' } })
    rerender(<LobbyHarness {...props} selectedRuleId="high"/>)
    expect((screen.getByLabelText('Table chips to buy in') as HTMLInputElement).value).toBe('500.00')
    rerender(<LobbyHarness {...props} selectedRuleId="high" balanceCredits={20.13}/>)
    expect((screen.getByLabelText('Table chips to buy in') as HTMLInputElement).value).toBe('20.13')
  })

  it('respects an explicit minimum buy-in and leaves amount controls disabled while busy', () => {
    const props = { rules: [{ ...testRule, minimumBuyInCredits: 5 }], selectedRuleId: 'standard', balanceCredits: 100, busy: false, onSelectRule: vi.fn(), onJoin: vi.fn() }
    const { rerender } = render(<LobbyHarness {...props}/>)
    expect((screen.getByRole('slider') as HTMLInputElement).min).toBe('500')
    rerender(<LobbyHarness {...props} busy/>)
    expect((screen.getByLabelText('Table chips to buy in') as HTMLInputElement).disabled).toBe(true)
    expect((screen.getByRole('slider') as HTMLInputElement).disabled).toBe(true)
    for (const button of screen.getAllByRole('button')) expect((button as HTMLButtonElement).disabled).toBe(true)
  })
})

describe('HoldemQueue', () => {
  it('offers cancel only when supplied and never permits a busy cancellation', () => {
    const onCancel = vi.fn()
    const { rerender } = render(<HoldemQueue busy />)
    expect(screen.getByRole('status').textContent).toContain('Finding table…')
    expect(screen.queryByRole('button', { name: 'Cancel' })).toBeNull()
    rerender(<HoldemQueue busy onCancel={onCancel} />)
    fireEvent.click(screen.getByRole('button', { name: 'Cancel' }))
    expect(onCancel).not.toHaveBeenCalled()
    rerender(<HoldemQueue busy={false} onCancel={onCancel} />)
    fireEvent.click(screen.getByRole('button', { name: 'Cancel' }))
    expect(onCancel).toHaveBeenCalledOnce()
  })
})
