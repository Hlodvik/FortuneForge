using FortuneForge.Server.Accounts;
using FortuneForge.Server.Accounts.Security;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace FortuneForge.Server.Risk;

public sealed record AccountReviewIncident(string Id, string GameId, string Severity, string Reason,
    string Status, DateTime CreatedAtUtc, DateTime? ResolvedAtUtc, string? ResolutionOutcome, string? ResolutionReason);
public sealed record AccountReviewStatus(bool AccountRestricted, string ReviewStatus, string Message,
    IReadOnlyList<AccountReviewIncident> Incidents);

[ApiController]
[Route("api/account/review")]
public sealed class AccountReviewController(AccountService accounts, IRiskStateReader risks, IServiceProvider services) : ControllerBase
{
    [HttpGet]
    [EnableRateLimiting(RateLimitPolicies.SlotReads)]
    public async Task<IActionResult> Status(CancellationToken ct)
    {
        var account = await accounts.GetAccessContextAsync(AccountSessionCookie.Read(Request), ct);
        if (account is null) return Unauthorized(new { error = "Sign in to check your account review." });
        var state = await risks.GetStateAsync(account.UserId, ct);
        // High-only restrictions remain undisclosed until the affected action.
        var incidents = state.AccountRestricted
            ? await services.GetRequiredService<FirestoreRiskIncidentStore>().ListContributingAsync(account.UserId, 100, ct)
            : Array.Empty<RiskIncident>();
        return Ok(SafeStatus(state, incidents));
    }

    internal static AccountReviewStatus SafeStatus(PlayerRiskState state, IReadOnlyList<RiskIncident> incidents)
    {
        if (!state.AccountRestricted) return new(false, "clear", "Your account is available.", []);
        var crossGame = state.CountsByGame.Count(pair => pair.Value.High >= 2) >= 2;
        var contributing = incidents.Where(incident => incident.UserId == state.UserId && PlayerRiskPolicy.CountsForRestrictions(incident)
                && (incident.Severity == RiskLevels.Critical || crossGame && incident.Severity == RiskLevels.High
                    && state.CountsByGame.TryGetValue(incident.GameId, out var counts) && counts.High >= 2))
            .Select(incident => new AccountReviewIncident(incident.Id, incident.GameId, incident.Severity, incident.AttributionBasis,
                incident.Status, incident.CreatedAtUtc, incident.ResolvedAtUtc, incident.ResolutionOutcome, incident.ResolutionReason)).ToArray();
        var pending = contributing.Any(incident => incident.Status == "open");
        var expected = state.CountsByGame.Values.Sum(count => count.Critical + (crossGame && count.High >= 2 ? count.High : 0));
        // A bounded list can omit an older contributing case; do not invent a
        // decision when its evidence is absent from this response.
        var status = pending ? "pending" : contributing.Length == 0 || contributing.Length < expected ? "restricted" : "confirmed";
        return new(true, status, pending ? "Your account is under review. The activity being reviewed is listed below."
            : status == "confirmed" ? "The account restriction was upheld. The review decision is listed below."
            : "Your account is restricted. Available review details are listed below.", contributing);
    }
}
