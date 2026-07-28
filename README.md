# FortuneForge

## Repository layout

- `FortuneForge.Server/Configuration` contains application startup, service registration, and middleware composition.
- `FortuneForge.Server/Accounts` owns authentication, profiles, account security, sessions, slot balances, and Firestore account persistence.
- `FortuneForge.Server/Payments` owns checkout and withdrawal workflows, MerchantGateway integration, signed webhooks, reconciliation, and Firestore payment persistence.
- `FortuneForge.Server/Slots` owns game definitions, reel generation, combination evaluation, payouts, bonuses, and spin orchestration.
- `FortuneForge.Server.Tests` mirrors the payment and slot feature boundaries.
- `fortuneforge.client/src/app` owns browser routing and shell composition; `features` owns account, payment, game-library, and slot workflows; `components` contains cross-feature presentation.
- `tools/FortuneForge.SlotMath` is the deterministic slot-math analysis console, while `scripts` contains asset and deployment automation.

## MerchantGateway payment integration

FortuneForge supports two server-side payment providers:

- `mock` for local interface testing without real transfers
- `merchantgateway` for merchant-scoped invoice creation, withdrawal creation, and status synchronization through MerchantGateway's `/api/v1` API

The React client continues to call FortuneForge's `/api/payments` endpoints. Only the FortuneForge ASP.NET server calls MerchantGateway, so the `x-merchant-api-key` credential must never be placed in client code, Firebase Hosting configuration, or source control.

Local development uses .NET user-secrets on `FortuneForge.Server`:

```powershell
dotnet user-secrets set "Payments:Provider" "merchantgateway" --project FortuneForge.Server
dotnet user-secrets set "Payments:MerchantGateway:BaseUrl" "http://localhost:<merchantgateway-http-port>" --project FortuneForge.Server
dotnet user-secrets set "Payments:MerchantGateway:ApiKey" "<merchantgateway-api-key>" --project FortuneForge.Server
dotnet user-secrets set "Payments:MerchantGateway:WebhookSigningSecrets:0" "<merchantgateway-callback-secret>" --project FortuneForge.Server
```

For production, supply the equivalent environment variables through the deployment secret manager:

```text
Payments__Provider=merchantgateway
Payments__MerchantGateway__BaseUrl=https://<merchantgateway-host>
Payments__MerchantGateway__ApiKey=<secret>
Payments__MerchantGateway__WebhookSigningSecrets__0=<secret-manager-value>
```

Optional pathway keys may be supplied as `Payments__MerchantGateway__PathwayKeys__ZA` and similar market-specific entries, but FortuneForge does not require customers to choose or know anything about receiving cards/routes. If no configured pathway key is provided, FortuneForge submits the invoice without one and lets MerchantGateway choose the merchant's active route.

The connector sends the FortuneForge invoice ID, amount, currency, generated customer reference, beneficiary reference, and an idempotency key to MerchantGateway. It translates invoice statuses into FortuneForge checkout statuses and credits slot credits only after the gateway reports `Completed`.

MerchantGateway posts signed events to `POST /api/payments/webhooks/merchantgateway`. FortuneForge verifies the timestamped HMAC signature, records the event ID for duplicate suppression, and then re-fetches the invoice through MerchantGateway. `invoice.processing` moves the checkout from received to processing; `invoice.completed` triggers the existing transactional, idempotent credit award. A hosted reconciliation worker independently checks pending invoices, so a lost webhook cannot strand a paid invoice. Firestore settlement remains transactional and idempotent.
