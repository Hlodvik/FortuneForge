import { useEffect, useState, type ChangeEvent, type FormEvent } from 'react'
import type { AccountSummary } from '../account/services/accountsApi'
import {
  createPaymentWithdrawal,
  type PaymentCatalog,
  type PaymentWithdrawal,
} from './services/paymentsApi'
import { creditsFormatter, errorMessage } from './paymentPresentation'

export function WithdrawalRequestCard({
  account,
  catalog,
  onAccountReload,
}: {
  account: AccountSummary
  catalog: PaymentCatalog
  onAccountReload: () => void
}) {
  const withdrawalMarket = catalog.markets.find((candidate) => candidate.code === 'ZA')
    ?? catalog.markets[0]
    ?? null
  const [amountInput, setAmountInput] = useState(String(withdrawalMarket?.minimumAmount ?? ''))
  const [firstName, setFirstName] = useState('')
  const [lastName, setLastName] = useState('')
  const [email, setEmail] = useState(account.email)
  const [accountHolder, setAccountHolder] = useState('')
  const [bankName, setBankName] = useState('')
  const [accountNumber, setAccountNumber] = useState('')
  const [branchCode, setBranchCode] = useState('')
  const [accountType, setAccountType] = useState('Cheque')
  const [withdrawalError, setWithdrawalError] = useState<string | null>(null)
  const [withdrawalResult, setWithdrawalResult] = useState<PaymentWithdrawal | null>(null)
  const [isSubmitting, setIsSubmitting] = useState(false)

  useEffect(() => {
    setEmail((currentEmail) => currentEmail.trim() === '' ? account.email : currentEmail)
  }, [account.email])

  const amount = amountInput === '' ? null : Number(amountInput)
  const isValidAmount = withdrawalMarket !== null
    && amount !== null
    && Number.isSafeInteger(amount)
    && amount >= withdrawalMarket.minimumAmount
    && amount <= withdrawalMarket.maximumAmount
  const creditsRequired = isValidAmount
    ? amount * withdrawalMarket.creditsPerCurrencyUnit
    : null
  const hasEnoughCredits = creditsRequired !== null
    && creditsRequired <= account.balances.slotsCredits
  const normalizedEmail = email.trim().toLowerCase()
  const hasValidCustomer = firstName.trim().length > 0
    && lastName.trim().length > 0
    && normalizedEmail === account.email.trim().toLowerCase()
  const hasValidBank = accountHolder.trim().length > 0
    && bankName.trim().length > 0
    && accountNumber.replace(/\D/g, '').length >= 5
    && branchCode.replace(/\D/g, '').length >= 3
    && accountType.trim().length > 0
  const canSubmit = withdrawalMarket !== null
    && isValidAmount
    && hasEnoughCredits
    && hasValidCustomer
    && hasValidBank
    && !isSubmitting

  function updateText(
    event: ChangeEvent<HTMLInputElement | HTMLSelectElement>,
    update: (value: string) => void,
    maxLength = 120,
  ) {
    update(event.target.value.slice(0, maxLength))
    setWithdrawalError(null)
    setWithdrawalResult(null)
  }

  function updateDigits(
    event: ChangeEvent<HTMLInputElement>,
    update: (value: string) => void,
    maxLength: number,
  ) {
    update(event.target.value.replace(/\D/g, '').slice(0, maxLength))
    setWithdrawalError(null)
    setWithdrawalResult(null)
  }

  async function handleWithdrawalSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    if (!canSubmit || withdrawalMarket === null || amount === null) {
      setWithdrawalError('Enter a valid customer, bank account, and payout amount that your current Rand balance can cover.')
      return
    }

    setIsSubmitting(true)
    setWithdrawalError(null)
    setWithdrawalResult(null)
    try {
      const created = await createPaymentWithdrawal(
        {
          market: withdrawalMarket.code,
          currency: withdrawalMarket.currency,
          amount,
          customerFirstName: firstName.trim(),
          customerLastName: lastName.trim(),
          customerEmail: normalizedEmail,
          accountHolder: accountHolder.trim(),
          bankName: bankName.trim(),
          accountNumber: accountNumber.replace(/\D/g, ''),
          branchCode: branchCode.replace(/\D/g, ''),
          accountType: accountType.trim(),
        },
        window.crypto.randomUUID(),
      )
      setWithdrawalResult(created)
      onAccountReload()
    } catch (requestError) {
      setWithdrawalError(errorMessage(requestError))
    } finally {
      setIsSubmitting(false)
    }
  }

  return (
    <section className="withdrawal-card" id="withdrawal-request" aria-labelledby="withdrawal-title">
      <header className="withdrawal-card__header">
        <div>
          <p className="account-eyebrow">Cash out</p>
          <h2 id="withdrawal-title">Withdrawal request</h2>
          <p>
            Request a South African bank payout from your Rand balance. The server reserves
            the Rand first, then sends the payout request to the payment provider.
          </p>
        </div>
        <span className="withdrawal-card__status">Live form</span>
      </header>

      <div className="withdrawal-card__facts" aria-label="Withdrawal rules">
        <article>
          <span>Account balance</span>
          <strong>R{creditsFormatter.format(account.balances.slotsCredits)}</strong>
          <small>Available Rand</small>
        </article>
        <article>
          <span>South African rate</span>
          <strong>R1 balance = R1 payout</strong>
          <small>{withdrawalMarket?.currency ?? 'ZAR'} payout amount is reserved one for one</small>
        </article>
        <article>
          <span>Reserved on submit</span>
          <strong>{creditsRequired === null ? '—' : `R${creditsFormatter.format(creditsRequired)}`}</strong>
          <small>{hasEnoughCredits || creditsRequired === null ? 'Rand required' : 'Not enough Rand'}</small>
        </article>
      </div>

      <form className="withdrawal-form" noValidate onSubmit={(event) => void handleWithdrawalSubmit(event)}>
        <section className="withdrawal-form__section">
          <div className="reload-card__section-heading">
            <div><p className="account-eyebrow">Payout amount</p><h3>How much should be withdrawn?</h3></div>
            <span>{withdrawalMarket?.displayName ?? 'South Africa'} · {withdrawalMarket?.currency ?? 'ZAR'}</span>
          </div>
          <label className="payment-amount__input withdrawal-form__amount" htmlFor="withdrawal-amount">
            <span>{withdrawalMarket?.currency ?? 'ZAR'}</span>
            <input
              id="withdrawal-amount"
              name="withdrawalAmount"
              type="text"
              inputMode="numeric"
              pattern="[0-9]*"
              autoComplete="off"
              required
              value={amountInput}
              aria-invalid={amountInput !== '' && (!isValidAmount || !hasEnoughCredits)}
              onChange={(event) => {
                const maxLength = withdrawalMarket === null ? 9 : String(withdrawalMarket.maximumAmount).length
                setAmountInput(event.target.value.replace(/\D/g, '').slice(0, maxLength))
                setWithdrawalError(null)
                setWithdrawalResult(null)
              }}
            />
          </label>
          <p className={amountInput !== '' && (!isValidAmount || !hasEnoughCredits) ? 'payment-amount__help is-error' : 'payment-amount__help'}>
            {withdrawalMarket === null
              ? 'Withdrawal market unavailable.'
              : `Enter ${withdrawalMarket.minimumAmount.toLocaleString()}–${withdrawalMarket.maximumAmount.toLocaleString()} ${withdrawalMarket.currency}.`}
            {' '}The account must have enough Rand to reserve the request.
          </p>
        </section>

        <section className="withdrawal-form__section">
          <div className="reload-card__section-heading">
            <div><p className="account-eyebrow">Customer identity</p><h3>Who receives this payout?</h3></div>
            <span>Sent with the withdrawal</span>
          </div>
          <div className="payment-customer__grid">
            <label className="payment-customer__field" htmlFor="withdrawal-first-name">
              <span>First name</span>
              <input id="withdrawal-first-name" type="text" autoComplete="given-name" required value={firstName} aria-invalid={firstName.trim() === ''} onChange={(event) => updateText(event, setFirstName, 80)} />
            </label>
            <label className="payment-customer__field" htmlFor="withdrawal-last-name">
              <span>Last name</span>
              <input id="withdrawal-last-name" type="text" autoComplete="family-name" required value={lastName} aria-invalid={lastName.trim() === ''} onChange={(event) => updateText(event, setLastName, 80)} />
            </label>
            <label className="payment-customer__field payment-customer__field--email" htmlFor="withdrawal-email">
              <span>Email / customer ID</span>
              <input id="withdrawal-email" type="email" autoComplete="email" required value={email} aria-invalid={normalizedEmail !== account.email.trim().toLowerCase()} onChange={(event) => updateText(event, setEmail, 254)} />
            </label>
          </div>
          <p className={hasValidCustomer ? 'payment-customer__help' : 'payment-customer__help is-error'}>
            Use the signed-in account email ({account.email}) so the withdrawal stays matched to this player.
          </p>
        </section>

        <section className="withdrawal-form__section">
          <div className="reload-card__section-heading">
            <div><p className="account-eyebrow">Bank destination</p><h3>Where should the payout be sent?</h3></div>
            <span>Required for payout</span>
          </div>
          <div className="withdrawal-form__bank-grid">
            <label className="payment-customer__field withdrawal-form__field--wide" htmlFor="withdrawal-account-holder">
              <span>Account holder</span>
              <input id="withdrawal-account-holder" type="text" autoComplete="name" required value={accountHolder} aria-invalid={accountHolder.trim() === ''} onChange={(event) => updateText(event, setAccountHolder)} />
            </label>
            <label className="payment-customer__field" htmlFor="withdrawal-bank-name">
              <span>Bank name</span>
              <input id="withdrawal-bank-name" type="text" required value={bankName} aria-invalid={bankName.trim() === ''} onChange={(event) => updateText(event, setBankName)} />
            </label>
            <label className="payment-customer__field" htmlFor="withdrawal-account-type">
              <span>Account type</span>
              <select id="withdrawal-account-type" required value={accountType} onChange={(event) => updateText(event, setAccountType, 40)}>
                <option value="Cheque">Cheque</option>
                <option value="Current">Current</option>
                <option value="Savings">Savings</option>
                <option value="Transmission">Transmission</option>
              </select>
            </label>
            <label className="payment-customer__field" htmlFor="withdrawal-account-number">
              <span>Account number</span>
              <input id="withdrawal-account-number" type="text" inputMode="numeric" required value={accountNumber} aria-invalid={accountNumber.replace(/\D/g, '').length < 5} onChange={(event) => updateDigits(event, setAccountNumber, 20)} />
            </label>
            <label className="payment-customer__field" htmlFor="withdrawal-branch-code">
              <span>Branch code</span>
              <input id="withdrawal-branch-code" type="text" inputMode="numeric" required value={branchCode} aria-invalid={branchCode.replace(/\D/g, '').length < 3} onChange={(event) => updateDigits(event, setBranchCode, 10)} />
            </label>
          </div>
        </section>

        {withdrawalError !== null && <p className="payment-checkout__error withdrawal-form__message" role="alert">{withdrawalError}</p>}
        {withdrawalResult !== null && (
          <p className="payment-checkout__result payment-checkout__result--success withdrawal-form__message" role="status">
            Withdrawal {withdrawalResult.withdrawalId} submitted. R{creditsFormatter.format(withdrawalResult.creditsDebited)} was reserved.
          </p>
        )}

        <button className="reload-card__checkout withdrawal-form__submit" type="submit" disabled={!canSubmit}>
          {isSubmitting ? 'Submitting withdrawal…' : 'Submit withdrawal'}
        </button>
      </form>
    </section>
  )
}
