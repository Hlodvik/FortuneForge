using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FortuneForge.Server.Payments.Configuration;
using FortuneForge.Server.Payments.Models;
using FortuneForge.Server.Payments.Storage;
using Microsoft.Extensions.Options;

namespace FortuneForge.Server.Payments.Providers;

internal sealed partial class MerchantGatewayPaymentProvider
{
    public async Task<PaymentResult<StoredPaymentWithdrawal>> CreateWithdrawalAsync(
        PaymentWithdrawalDraft draft,
        CancellationToken cancellationToken)
    {
        StoredPaymentWithdrawal? reservation = null;
        try
        {
            var client = httpClientFactory.CreateClient(HttpClientName);
            var pathway = await TryGetPreferredPathwayAsync(client, draft.Market.Code, cancellationToken);

            var createdAtUtc = DateTime.UtcNow;
            var localWithdrawal = new StoredPaymentWithdrawal(
                draft.WithdrawalId,
                string.Empty,
                NormalizePathwayKey(pathway?.Key),
                draft.UserId,
                draft.IdempotencyKey,
                Id,
                false,
                draft.Market,
                draft.Amount,
                draft.AmountMinor,
                draft.CreditsDebited,
                "received",
                createdAtUtc,
                draft.CreatedAtUtc,
                null,
                draft.Customer,
                draft.Bank,
                "Withdrawal request reserved locally. Payout status is pending.");
            var reservationResult = await paymentStore.CreateWithdrawalReservationAsync(
                localWithdrawal,
                cancellationToken);
            if (reservationResult.Value is null)
            {
                return reservationResult;
            }

            reservation = reservationResult.Value;
            var withdrawal = reservation;
            if (!string.IsNullOrWhiteSpace(withdrawal.ProviderWithdrawalId) ||
                withdrawal.Status == "completed")
            {
                return PaymentResult<StoredPaymentWithdrawal>.Success(withdrawal);
            }

            if (WithdrawalStatusProjection.IsNegativeTerminal(withdrawal.Status))
            {
                return PaymentResult<StoredPaymentWithdrawal>.Failure(PaymentError.ProviderRejected);
            }

            var isUncertainReplay =
                withdrawal.Status == "pending" &&
                string.IsNullOrWhiteSpace(withdrawal.ProviderWithdrawalId);
            var submittedPathwayKey = isUncertainReplay
                ? NormalizePathwayKey(withdrawal.ProviderPathwayKey)
                : NormalizePathwayKey(withdrawal.ProviderPathwayKey) ??
                    NormalizePathwayKey(pathway?.Key);
            using var request = CreateRequest(HttpMethod.Post, "api/v1/withdrawals");
            request.Headers.TryAddWithoutValidation("Idempotency-Key", withdrawal.IdempotencyKey);
            request.Content = JsonContent.Create(new MerchantGatewayWithdrawalCreateRequest(
                withdrawal.WithdrawalId,
                withdrawal.Amount,
                withdrawal.Market.Currency,
                submittedPathwayKey,
                withdrawal.Bank.AccountHolder,
                withdrawal.Bank.BankName,
                withdrawal.Bank.AccountNumber,
                withdrawal.Bank.BranchCode,
                withdrawal.Bank.AccountType));

            using var response = await client.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var responseBody = await LogProviderRejectionAsync(
                    response,
                    "withdrawal",
                    withdrawal.WithdrawalId,
                    cancellationToken);
                if (!IsAuthoritativeWithdrawalCreateRejection(
                    response.StatusCode,
                    responseBody,
                    isUncertainReplay))
                {
                    return await MarkWithdrawalSubmissionUncertainAsync(
                        withdrawal,
                        cancellationToken);
                }

                var failed = await paymentStore.FailWithdrawalReservationAsync(
                    withdrawal.WithdrawalId,
                    withdrawal.UserId,
                    DateTime.UtcNow,
                    cancellationToken);
                if (failed.Value is null)
                {
                    return failed;
                }

                return PaymentResult<StoredPaymentWithdrawal>.Failure(
                    MapProviderError(response.StatusCode, responseBody));
            }

            var created = await response.Content.ReadFromJsonAsync<MerchantGatewayCreatedResponse>(
                cancellationToken);
            if (created is null || created.Id == Guid.Empty)
            {
                logger.LogError("MerchantGateway returned an invalid withdrawal creation response.");
                return await MarkWithdrawalSubmissionUncertainAsync(
                    withdrawal,
                    cancellationToken);
            }

            return await paymentStore.UpdateWithdrawalProviderAsync(
                withdrawal.WithdrawalId,
                withdrawal.UserId,
                created.Id.ToString("N"),
                MapWithdrawalStatus(created.Status, "pending"),
                DateTime.UtcNow,
                cancellationToken);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("MerchantGateway timed out while creating a withdrawal.");
            return await MarkWithdrawalSubmissionUncertainAsync(
                reservation,
                CancellationToken.None);
        }
        catch (HttpRequestException exception)
        {
            logger.LogWarning(exception, "MerchantGateway was unavailable while creating a withdrawal.");
            return await MarkWithdrawalSubmissionUncertainAsync(
                reservation,
                CancellationToken.None);
        }
        catch (JsonException exception)
        {
            logger.LogError(exception, "MerchantGateway returned invalid JSON while creating a withdrawal.");
            return await MarkWithdrawalSubmissionUncertainAsync(
                reservation,
                CancellationToken.None);
        }
    }
}
