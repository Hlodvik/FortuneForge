import { fetchWithAccountSession } from '../../account/services/accountsApi'

export type PaymentMethodOption = {
  id: string
  type: string
  displayName: string
  description: string
  settlementLabel: string
}

export type PaymentMarketOption = {
  code: string
  displayName: string
  currency: string
  locale: string
  audienceLabel: string
  paymentNotice: string
  minimumAmount: number
  maximumAmount: number
  creditsPerCurrencyUnit: number
  suggestedAmounts: number[]
  paymentMethods: PaymentMethodOption[]
}

export type PaymentCatalog = {
  providerId: string
  isMock: boolean
  mockSimulationEnabled: boolean
  markets: PaymentMarketOption[]
}

export type BankTransferInstructions = {
  bankName: string
  accountName: string
  accountNumber: string
  branchCode: string
  reference: string
  instructions: string
}

export type PaymentStatus = 'received' | 'processing' | 'completed' | 'failed' | 'expired'

export type PaymentCheckout = {
  checkoutId: string
  providerCheckoutId: string
  invoiceId: string
  userId: string
  providerId: string
  isMock: boolean
  market: string
  marketName: string
  currency: string
  locale: string
  paymentMethodId: string
  paymentMethodName: string
  paymentMethodType: string
  amount: number
  amountMinor: number
  credits: number
  status: PaymentStatus
  statusUpdatedAtUtc: string
  createdAtUtc: string
  expiresAtUtc: string
  processingAtUtc: string | null
  completedAtUtc: string | null
  creditedBalance: number | null
  customer: PaymentCustomerDetails
  payerBank?: PaymentBankDetails | null
  bankTransfer: BankTransferInstructions | null
  notice: string
}

export type PaymentCustomerDetails = {
  firstName: string
  lastName: string
  email: string
  customerReference: string
  beneficiaryReference: string
}

export type CreatePaymentCheckoutInput = {
  market: string
  currency: string
  paymentMethodId: string
  amount: number
  customerFirstName: string
  customerLastName: string
  customerEmail: string
  accountHolder: string
  bankName: string
  accountNumber: string
  branchCode: string
  accountType: string
}

export type PaymentBankDetails = {
  accountHolder: string
  bankName: string
  accountNumber: string
  branchCode: string
  accountType: string
}

export type PaymentWithdrawal = {
  withdrawalId: string
  providerWithdrawalId: string
  userId: string
  providerId: string
  isMock: boolean
  market: string
  marketName: string
  currency: string
  locale: string
  amount: number
  amountMinor: number
  creditsDebited: number
  status: string
  statusUpdatedAtUtc: string
  createdAtUtc: string
  completedAtUtc: string | null
  customer: PaymentCustomerDetails
  bank: PaymentBankDetails
  notice: string
}

export type CreatePaymentWithdrawalInput = {
  market: string
  currency: string
  amount: number
  customerFirstName: string
  customerLastName: string
  customerEmail: string
  accountHolder: string
  bankName: string
  accountNumber: string
  branchCode: string
  accountType: string
}

type PaymentInvoiceList = {
  invoices: PaymentCheckout[]
}

type ProblemDetails = {
  title?: string
  detail?: string
}

export class PaymentRequestError extends Error {
  readonly status: number

  constructor(message: string, status: number) {
    super(message)
    this.name = 'PaymentRequestError'
    this.status = status
  }
}

export function getPaymentCatalog(): Promise<PaymentCatalog> {
  return paymentRequest<PaymentCatalog>('/api/payments/catalog', { method: 'GET' })
}

export function createPaymentCheckout(
  input: CreatePaymentCheckoutInput,
  idempotencyKey: string,
): Promise<PaymentCheckout> {
  return paymentRequest<PaymentCheckout>('/api/payments/checkouts', {
    method: 'POST',
    headers: { 'Idempotency-Key': idempotencyKey },
    body: JSON.stringify(input),
  })
}

export function createPaymentWithdrawal(
  input: CreatePaymentWithdrawalInput,
  idempotencyKey: string,
): Promise<PaymentWithdrawal> {
  return paymentRequest<PaymentWithdrawal>('/api/payments/withdrawals', {
    method: 'POST',
    headers: { 'Idempotency-Key': idempotencyKey },
    body: JSON.stringify(input),
  })
}

export function getPaymentInvoice(invoiceId: string): Promise<PaymentCheckout> {
  return paymentRequest<PaymentCheckout>(
    `/api/payments/invoices/${encodeURIComponent(invoiceId)}`,
    { method: 'GET' },
  )
}

export async function listPaymentInvoices(limit = 20): Promise<PaymentCheckout[]> {
  const result = await paymentRequest<PaymentInvoiceList>(
    `/api/payments/invoices?limit=${encodeURIComponent(limit)}`,
    { method: 'GET' },
  )
  return result.invoices
}

export async function listAdminPaymentInvoices(
  userId: string,
  limit = 50,
): Promise<PaymentCheckout[]> {
  const result = await paymentRequest<PaymentInvoiceList>(
    `/api/payments/admin/users/${encodeURIComponent(userId)}/invoices?limit=${encodeURIComponent(limit)}`,
    { method: 'GET' },
  )
  return result.invoices
}

export function getAdminPaymentInvoice(invoiceId: string): Promise<PaymentCheckout> {
  return paymentRequest<PaymentCheckout>(
    `/api/payments/admin/invoices/${encodeURIComponent(invoiceId)}`,
    { method: 'GET' },
  )
}

export function simulateMockPayment(
  checkoutId: string,
  status: 'processing' | 'completed' | 'failed',
): Promise<PaymentCheckout> {
  return paymentRequest<PaymentCheckout>(
    `/api/payments/mock/checkouts/${encodeURIComponent(checkoutId)}/simulate`,
    {
      method: 'POST',
      body: JSON.stringify({ status }),
    },
  )
}

async function paymentRequest<T>(path: string, init: RequestInit): Promise<T> {
  const response = await fetchWithAccountSession(path, init)
  if (!response.ok) {
    const problem = await response.json().catch(() => null) as ProblemDetails | null
    throw new PaymentRequestError(
      problem?.detail ?? problem?.title ?? `Payment request failed (${response.status}).`,
      response.status,
    )
  }

  return response.json() as Promise<T>
}
