import { useEffect, useState, type ChangeEvent, type FormEvent } from 'react'
import { ForgeCoin, ForgeCreditAmount } from '../../components/ForgeCreditAmount'
import { PaymentAlertsMenu } from '../../components/PaymentAlertsMenu'
import { useAuthenticatedAccount } from '../landing/useAuthenticatedAccount'
import '../landing/landing.css'
import { creditsFormatter, errorMessage, formatMoney } from './paymentPresentation'
import { WithdrawalRequestCard } from './WithdrawalRequestCard'
import {
  createPaymentCheckout,
  getPaymentCatalog,
} from './services/paymentsApi'
import type { PaymentMarketOption } from './services/paymentsApi'


export function PurchaseCreditsPage() {
  const { account, error: accountError, isLoading: isAccountLoading, reload } =
    useAuthenticatedAccount('/home/credits')
  const [catalog, setCatalog] = useState<Awaited<ReturnType<typeof getPaymentCatalog>> | null>(null)
  const [catalogError, setCatalogError] = useState<string | null>(null)
  const [selectedMarketCode, setSelectedMarketCode] = useState('')
  const [selectedMethodId, setSelectedMethodId] = useState('')
  const [amountInput, setAmountInput] = useState('')
  const [customerFirstName, setCustomerFirstName] = useState('')
  const [customerLastName, setCustomerLastName] = useState('')
  const [customerEmail, setCustomerEmail] = useState('')
  const [accountHolder, setAccountHolder] = useState('')
  const [bankName, setBankName] = useState('')
  const [accountNumber, setAccountNumber] = useState('')
  const [branchCode, setBranchCode] = useState('')
  const [accountType, setAccountType] = useState('Cheque')
  const [checkoutError, setCheckoutError] = useState<string | null>(null)
  const [isSubmitting, setIsSubmitting] = useState(false)

  useEffect(() => {
    if (account === null) {
      return
    }

    let active = true
    setCatalogError(null)
    void getPaymentCatalog()
      .then((nextCatalog) => {
        if (!active) {
          return
        }

        const firstMarket = nextCatalog.markets[0]
        setCatalog(nextCatalog)
        setSelectedMarketCode(firstMarket?.code ?? '')
        setSelectedMethodId(firstMarket?.paymentMethods[0]?.id ?? '')
        setAmountInput(String(firstMarket?.suggestedAmounts[2] ?? firstMarket?.minimumAmount ?? ''))
      })
      .catch((requestError: unknown) => {
        if (active) {
          setCatalogError(errorMessage(requestError))
        }
      })

    return () => {
      active = false
    }
  }, [account])

  useEffect(() => {
    if (account === null) {
      return
    }

    setCustomerEmail((currentEmail) => currentEmail.trim() === '' ? account.email : currentEmail)
  }, [account])

  const market = catalog?.markets.find((candidate) => candidate.code === selectedMarketCode) ?? null
  const selectedMethod = market?.paymentMethods.find((candidate) => candidate.id === selectedMethodId) ?? null
  const amount = amountInput === '' ? null : Number(amountInput)
  const isValidAmount = market !== null
    && amount !== null
    && Number.isSafeInteger(amount)
    && amount >= market.minimumAmount
    && amount <= market.maximumAmount
  const credits = isValidAmount ? amount * market.creditsPerCurrencyUnit : null
  const normalizedCustomerEmail = customerEmail.trim().toLowerCase()
  const hasValidCustomerDetails = account !== null
    && customerFirstName.trim().length > 0
    && customerLastName.trim().length > 0
    && normalizedCustomerEmail === account.email.trim().toLowerCase()
  const hasValidBankDetails = accountHolder.trim().length > 0
    && bankName.trim().length > 0
    && accountNumber.replace(/\D/g, '').length >= 5
    && branchCode.replace(/\D/g, '').length >= 3
    && accountType.trim().length > 0
  const canSubmit = market !== null
    && selectedMethod !== null
    && isValidAmount
    && hasValidCustomerDetails
    && hasValidBankDetails
    && !isSubmitting

  function chooseMarket(nextMarket: PaymentMarketOption) {
    setSelectedMarketCode(nextMarket.code)
    setSelectedMethodId(nextMarket.paymentMethods[0]?.id ?? '')
    setCheckoutError(null)
  }

  function handleAmountChange(event: ChangeEvent<HTMLInputElement>) {
    const digitsOnly = event.target.value.replace(/\D/g, '')
    const maximumLength = market === null ? 9 : String(market.maximumAmount).length
    setAmountInput(digitsOnly.slice(0, maximumLength))
    setCheckoutError(null)
  }

  function handleCustomerTextChange(
    event: ChangeEvent<HTMLInputElement | HTMLSelectElement>,
    update: (value: string) => void,
    maxLength = 80,
  ) {
    update(event.target.value.slice(0, maxLength))
    setCheckoutError(null)
  }

  function handleDigitsChange(
    event: ChangeEvent<HTMLInputElement>,
    update: (value: string) => void,
    maxLength: number,
  ) {
    update(event.target.value.replace(/\D/g, '').slice(0, maxLength))
    setCheckoutError(null)
  }

  async function handleCreateCheckout(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    if (!canSubmit || market === null || selectedMethod === null || amount === null) {
      setCheckoutError('Enter the customer name, signed-in email, bank details, payment market, method, and a valid whole-number amount.')
      return
    }

    setIsSubmitting(true)
    setCheckoutError(null)
    try {
      const createdCheckout = await createPaymentCheckout(
        {
          market: market.code,
          currency: market.currency,
          paymentMethodId: selectedMethod.id,
          amount,
          customerFirstName: customerFirstName.trim(),
          customerLastName: customerLastName.trim(),
          customerEmail: normalizedCustomerEmail,
          accountHolder: accountHolder.trim(),
          bankName: bankName.trim(),
          accountNumber: accountNumber.replace(/\D/g, ''),
          branchCode: branchCode.replace(/\D/g, ''),
          accountType: accountType.trim(),
        },
        window.crypto.randomUUID(),
      )
      window.location.assign(`/home/invoices/${encodeURIComponent(createdCheckout.invoiceId)}`)
    } catch (requestError) {
      setCheckoutError(errorMessage(requestError))
      setIsSubmitting(false)
    }
  }

  return (
    <div className="player-page player-page--credits">
      <header className="landing-bar">
        <a className="landing-brand" href="/" aria-label="Fortune Forge home">
          <span className="landing-brand__spark" aria-hidden="true">✦</span>
          <span>Fortune Forge</span>
        </a>
        <div className="landing-bar__account">
          {account !== null && <ForgeCreditAmount amount={account.balances.slotsCredits} />}
          <PaymentAlertsMenu />
          <nav className="landing-nav" aria-label="Credit account navigation">
            <a className="landing-nav__link" href="/home">Home</a>
            <a className="landing-nav__link" href="#withdrawal-request">Withdraw</a>
            <a className="landing-nav__link" href="/slots">Games</a>
          </nav>
        </div>
      </header>

      <main className="reload-main">
        {isAccountLoading && <PageState label="Opening the credit forge…" />}
        {!isAccountLoading && accountError !== null && (
          <PageError message={accountError} onRetry={reload} />
        )}
        {!isAccountLoading && account !== null && catalog === null && catalogError === null && (
          <PageState label="Loading regional payment options…" />
        )}
        {!isAccountLoading && account !== null && catalogError !== null && (
          <PageError message={catalogError} onRetry={() => window.location.reload()} />
        )}
        {!isAccountLoading && account !== null && catalog !== null && market !== null && (
          <>
          <article className="reload-card" id="purchase-credits">
            <section className="reload-card__hero">
              <div className="reload-card__hero-copy">
                <p className="account-eyebrow">Regional credit purchase</p>
                <h1>Refill your fortune.</h1>
                <p className="reload-card__intro">
                  Enter the exact whole-number amount you want to load. The server calculates the
                  credit total and creates a persistent invoice linked to your account.
                </p>
                {catalog.isMock && (
                  <div className="reload-card__preview-note" role="note">
                    <span aria-hidden="true">⚠</span>
                    <p><strong>Mock checkout</strong>Do not send real money to the test bank details.</p>
                  </div>
                )}
              </div>
              <div className="reload-card__coin-vault" aria-hidden="true">
                <span className="reload-card__orbit reload-card__orbit--outer" />
                <span className="reload-card__orbit reload-card__orbit--inner" />
                <span className="reload-card__coin"><ForgeCoin /></span>
                <small>Fortune credits</small>
              </div>
            </section>

            <section className="reload-card__balances" aria-label="Current purchase context">
              <article><span>Current balance</span><strong>{creditsFormatter.format(account.balances.slotsCredits)}</strong><small>Fortune credits</small></article>
              <article><span>Selected market</span><strong>{market.code}</strong><small>{market.displayName}</small></article>
              <article><span>Payment currency</span><strong>{market.currency}</strong><small>Recorded on the invoice</small></article>
            </section>

            <form className="payment-form" noValidate onSubmit={(event) => void handleCreateCheckout(event)}>
              <section className="reload-card__section" aria-labelledby="payment-market-title">
                <div className="reload-card__section-heading">
                  <div><p className="account-eyebrow">Country and currency</p><h2 id="payment-market-title">Where are you paying from?</h2></div>
                  <span>{market.audienceLabel}</span>
                </div>
                <div className="reload-card__markets">
                  {catalog.markets.map((candidate) => (
                    <button
                      key={candidate.code}
                      type="button"
                      className={candidate.code === market.code ? 'is-selected' : undefined}
                      aria-pressed={candidate.code === market.code}
                      onClick={() => chooseMarket(candidate)}
                    >
                      <small>{candidate.code}</small><strong>{candidate.displayName}</strong><span>{candidate.currency}</span><p>{candidate.audienceLabel}</p>
                    </button>
                  ))}
                </div>
                <p className="reload-card__market-notice">{market.paymentNotice}</p>
              </section>

              <section className="reload-card__section payment-customer" aria-labelledby="payment-customer-title">
                <div className="reload-card__section-heading">
                  <div><p className="account-eyebrow">Customer details</p><h2 id="payment-customer-title">Who is this payment for?</h2></div>
                  <span>Sent with the invoice</span>
                </div>
                <div className="payment-customer__grid">
                  <label className="payment-customer__field" htmlFor="payment-customer-first-name">
                    <span>First name</span>
                    <input
                      id="payment-customer-first-name"
                      name="customerFirstName"
                      type="text"
                      autoComplete="given-name"
                      required
                      value={customerFirstName}
                      aria-invalid={customerFirstName.trim() === ''}
                      onChange={(event) => handleCustomerTextChange(event, setCustomerFirstName)}
                    />
                  </label>
                  <label className="payment-customer__field" htmlFor="payment-customer-last-name">
                    <span>Last name</span>
                    <input
                      id="payment-customer-last-name"
                      name="customerLastName"
                      type="text"
                      autoComplete="family-name"
                      required
                      value={customerLastName}
                      aria-invalid={customerLastName.trim() === ''}
                      onChange={(event) => handleCustomerTextChange(event, setCustomerLastName)}
                    />
                  </label>
                  <label className="payment-customer__field payment-customer__field--email" htmlFor="payment-customer-email">
                    <span>Email / customer ID</span>
                    <input
                      id="payment-customer-email"
                      name="customerEmail"
                      type="email"
                      autoComplete="email"
                      required
                      value={customerEmail}
                      aria-invalid={account !== null && normalizedCustomerEmail !== account.email.trim().toLowerCase()}
                      onChange={(event) => {
                        setCustomerEmail(event.target.value.slice(0, 254))
                        setCheckoutError(null)
                      }}
                    />
                  </label>
                </div>
                <p className={hasValidCustomerDetails ? 'payment-customer__help' : 'payment-customer__help is-error'}>
                  Use the signed-in account email ({account.email}) so the invoice stays matched to this player.
                </p>
              </section>

              <section className="reload-card__section payment-customer" aria-labelledby="payment-bank-title">
                <div className="reload-card__section-heading">
                  <div><p className="account-eyebrow">Payment bank details</p><h2 id="payment-bank-title">Which account will send the payment?</h2></div>
                  <span>Sent with the invoice</span>
                </div>
                <div className="withdrawal-form__bank-grid">
                  <label className="payment-customer__field withdrawal-form__field--wide" htmlFor="payment-account-holder">
                    <span>Account holder</span>
                    <input
                      id="payment-account-holder"
                      name="accountHolder"
                      type="text"
                      autoComplete="name"
                      required
                      value={accountHolder}
                      aria-invalid={accountHolder.trim() === ''}
                      onChange={(event) => handleCustomerTextChange(event, setAccountHolder, 120)}
                    />
                  </label>
                  <label className="payment-customer__field" htmlFor="payment-bank-name">
                    <span>Bank name</span>
                    <input
                      id="payment-bank-name"
                      name="bankName"
                      type="text"
                      required
                      value={bankName}
                      aria-invalid={bankName.trim() === ''}
                      onChange={(event) => handleCustomerTextChange(event, setBankName, 120)}
                    />
                  </label>
                  <label className="payment-customer__field" htmlFor="payment-account-type">
                    <span>Account type</span>
                    <select
                      id="payment-account-type"
                      name="accountType"
                      required
                      value={accountType}
                      onChange={(event) => handleCustomerTextChange(event, setAccountType, 40)}
                    >
                      <option value="Cheque">Cheque</option>
                      <option value="Current">Current</option>
                      <option value="Savings">Savings</option>
                      <option value="Transmission">Transmission</option>
                    </select>
                  </label>
                  <label className="payment-customer__field" htmlFor="payment-account-number">
                    <span>Account number</span>
                    <input
                      id="payment-account-number"
                      name="accountNumber"
                      type="text"
                      inputMode="numeric"
                      required
                      value={accountNumber}
                      aria-invalid={accountNumber.replace(/\D/g, '').length < 5}
                      onChange={(event) => handleDigitsChange(event, setAccountNumber, 20)}
                    />
                  </label>
                  <label className="payment-customer__field" htmlFor="payment-branch-code">
                    <span>Branch code</span>
                    <input
                      id="payment-branch-code"
                      name="branchCode"
                      type="text"
                      inputMode="numeric"
                      required
                      value={branchCode}
                      aria-invalid={branchCode.replace(/\D/g, '').length < 3}
                      onChange={(event) => handleDigitsChange(event, setBranchCode, 10)}
                    />
                  </label>
                </div>
                <p className={hasValidBankDetails ? 'payment-customer__help' : 'payment-customer__help is-error'}>
                  Add the bank account details tied to the transfer so support can match the payment cleanly.
                </p>
              </section>

              <section className="reload-card__section payment-amount" aria-labelledby="payment-amount-title">
                <div className="reload-card__section-heading">
                  <div><p className="account-eyebrow">Load amount</p><h2 id="payment-amount-title">How much would you like to load?</h2></div>
                  <span>Whole numbers only</span>
                </div>
                <label className="payment-amount__input" htmlFor="payment-amount">
                  <span>{market.currency}</span>
                  <input
                    id="payment-amount"
                    name="amount"
                    type="text"
                    inputMode="numeric"
                    pattern="[0-9]*"
                    autoComplete="off"
                    required
                    value={amountInput}
                    aria-invalid={amountInput !== '' && !isValidAmount}
                    aria-describedby="payment-amount-help"
                    onChange={handleAmountChange}
                  />
                </label>
                <div className="payment-amount__suggestions" aria-label="Suggested amounts">
                  {market.suggestedAmounts.map((suggestedAmount) => (
                    <button
                      key={suggestedAmount}
                      type="button"
                      className={amount === suggestedAmount ? 'is-selected' : undefined}
                      aria-pressed={amount === suggestedAmount}
                      onClick={() => {
                        setAmountInput(String(suggestedAmount))
                        setCheckoutError(null)
                      }}
                    >
                      {formatMoney(market, suggestedAmount * 100)}
                    </button>
                  ))}
                </div>
                <p id="payment-amount-help" className={amountInput !== '' && !isValidAmount ? 'payment-amount__help is-error' : 'payment-amount__help'}>
                  Enter {market.minimumAmount.toLocaleString()}–{market.maximumAmount.toLocaleString()} {market.currency}. Letters, punctuation, and decimals are ignored.
                </p>
              </section>

              <div className="reload-card__checkout-grid">
                <section className="reload-card__section reload-card__payment-section" aria-labelledby="payment-method-title">
                  <div className="reload-card__section-heading"><div><p className="account-eyebrow">Payment method</p><h2 id="payment-method-title">How will you pay?</h2></div></div>
                  {market.paymentMethods.map((method) => (
                    <button
                      className={`reload-card__payment${method.id === selectedMethod?.id ? ' is-selected' : ''}`}
                      key={method.id}
                      type="button"
                      aria-pressed={method.id === selectedMethod?.id}
                      onClick={() => {
                        setSelectedMethodId(method.id)
                        setCheckoutError(null)
                      }}
                    >
                      <span className="reload-card__payment-icon" aria-hidden="true">↔</span>
                      <span>{method.type.replace('_', ' ')}<strong>{method.displayName}</strong></span>
                      <em>{method.settlementLabel}</em>
                    </button>
                  ))}
                  <p className="reload-card__payment-copy">{selectedMethod?.description}</p>
                </section>

                <section className="reload-card__summary" aria-labelledby="order-summary-title">
                  <h2 id="order-summary-title">Checkout summary</h2>
                  <dl>
                    <div><dt>Amount</dt><dd>{isValidAmount ? formatMoney(market, amount * 100) : '—'}</dd></div>
                    <div><dt>Credits</dt><dd>{credits === null ? '—' : creditsFormatter.format(credits)}</dd></div>
                    <div><dt>Customer</dt><dd>{customerFirstName.trim() || customerLastName.trim() ? `${customerFirstName.trim()} ${customerLastName.trim()}`.trim() : '—'}</dd></div>
                    <div><dt>Payer bank</dt><dd>{bankName.trim() || '—'}</dd></div>
                    <div><dt>Market</dt><dd>{market.displayName}</dd></div>
                    <div><dt>Method</dt><dd>{selectedMethod?.displayName ?? '—'}</dd></div>
                  </dl>
                  <button className="reload-card__checkout" type="submit" disabled={!canSubmit}>
                    {isSubmitting ? 'Submitting payment…' : 'Submit payment'}
                  </button>
                  <small>After the API receives the order, you’ll be redirected to its invoice.</small>
                </section>
              </div>

              {checkoutError !== null && <p className="payment-checkout__error" role="alert">{checkoutError}</p>}
            </form>

            <footer className="reload-card__assurances">
              <span><i>1</i><strong>Server-owned conversion</strong>The API validates the amount and calculates credits.</span>
              <span><i>2</i><strong>Duplicate-safe requests</strong>Each checkout uses an idempotency key.</span>
              <span><i>3</i><strong>Completion-only crediting</strong>Received or processing invoices never change the balance.</span>
            </footer>
          </article>
          <WithdrawalRequestCard account={account} catalog={catalog} onAccountReload={reload} />
          </>
        )}
      </main>
    </div>
  )
}

function PageState({ label }: { label: string }) {
  return <div className="player-state" role="status"><span aria-hidden="true">✦</span>{label}</div>
}

function PageError({ message, onRetry }: { message: string; onRetry: () => void }) {
  return (
    <div className="player-state player-state--error" role="alert">
      <strong>The credit purchase page could not be opened.</strong><span>{message}</span>
      <button className="landing-button landing-button--secondary" type="button" onClick={onRetry}>Try again</button>
      <a href="/home">Return home</a>
    </div>
  )
}
