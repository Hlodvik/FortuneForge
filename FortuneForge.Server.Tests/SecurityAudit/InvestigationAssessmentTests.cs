using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Server.SecurityAudit;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace FortuneForge.Server.Tests.SecurityAudit;

public sealed class InvestigationAssessmentTests
{
    private static readonly DateTime Now = new(2026, 10, 26, 20, 0, 0, DateTimeKind.Utc);
    private static readonly SecurityAuditInvestigationOptions Options = new();
    private static readonly SecurityAuditInvestigationOptions VerifiedNetworkOptions = new() { EnableVerifiedNetworkPairPriority = true };

    [Fact]
    public void BaselinePairScreeningDoesNotRequireAnyNetworkLinkOrClaimCalibratedCheatingProbability()
    {
        var absent = InvestigationAssessment.Assess(20, 10, [], "alice", "bob", Options);
        var different = InvestigationAssessment.Assess(20, 10,
            [new("alice", "192.0.2.1", Now), new("bob", "198.51.100.2", Now)], "alice", "bob", Options);
        Assert.False(absent.ReviewCandidate);
        Assert.False(different.ReviewCandidate);
        Assert.True(absent.NeedsDeeperAnalysis);
        Assert.True(different.NeedsDeeperAnalysis);
        Assert.Equal("needs-strategy-analysis", absent.Coverage);
        Assert.Contains(absent.Signals, value => value.Code == "network-not-exclusionary");
        Assert.DoesNotContain(absent.Signals, value => value.Confidence == "confirmed-cheating");
    }

    [Fact]
    public void SharedAddressAndApproximateCityOnlyRaiseReviewPriorityAndCannotOvercomeInsufficientPlay()
    {
        var observations = new InvestigationNetworkObservation[]
        {
            new("alice", "192.0.2.1", Now, "Example City", "provider", Now, 50, "city-approximate", "US", "IL", "trusted-proxy-chain"),
            new("bob", "192.0.2.1", Now.AddSeconds(30), "Example City", "provider", Now, 50, "city-approximate", "US", "IL", "trusted-proxy-chain")
        };
        var result = InvestigationAssessment.Assess(1, 1, observations, "alice", "bob", VerifiedNetworkOptions);
        Assert.False(result.ReviewCandidate);
        Assert.Equal("needs-strategy-analysis", result.Coverage);
        Assert.True(result.NeedsDeeperAnalysis);
        Assert.Equal(3, result.ReviewPriority);
        Assert.Contains(result.Signals, value => value.Code == "shared-ip-overlap" && value.Confidence == "corroboration-only");
        Assert.Contains(result.Signals, value => value.Code == "same-city" && value.Confidence == "weak");
    }

    [Fact]
    public void SameEstimatedCityWithDifferentAddressesRequestsExtraScrutinyWithoutAPlayerFinding()
    {
        var result = InvestigationAssessment.Assess(1, 1,
            [new("alice", "192.0.2.1", Now, "Example City", "provider", Now, 50, "approximate", "US", "IL", "trusted-proxy-chain"),
             new("bob", "198.51.100.2", Now, "Example City", "provider", Now, 50, "approximate", "US", "IL", "trusted-proxy-chain")],
            "alice", "bob", VerifiedNetworkOptions);
        Assert.True(result.NeedsDeeperAnalysis);
        Assert.False(result.ReviewCandidate);
        Assert.Equal(1, result.ReviewPriority);
        Assert.DoesNotContain(result.Signals, signal => signal.Code == "shared-ip-overlap");
    }

    [Fact]
    public void StaleSharedAddressDoesNotCountAsOverlappingPlayAndOtherUsersDoNotCorroboratePair()
    {
        var result = InvestigationAssessment.Assess(20, 10,
            [new("alice", "192.0.2.1", Now, IpSource: "trusted-proxy-chain"), new("bob", "192.0.2.1", Now.AddDays(1), IpSource: "trusted-proxy-chain"),
             new("outsider", "192.0.2.1", Now, IpSource: "trusted-proxy-chain")],
            "alice", "bob", VerifiedNetworkOptions);
        Assert.DoesNotContain(result.Signals, value => value.Code == "shared-ip-overlap");
    }

    [Fact]
    public void SameNamedCitiesInDifferentCountriesOrRegionsDoNotCountAsLocationCorroboration()
    {
        var result = InvestigationAssessment.Assess(1, 1,
            [new("alice", null, Now, "Springfield", Country: "US", Region: "IL", IpSource: "trusted-proxy-chain"),
             new("bob", null, Now, "Springfield", Country: "US", Region: "MA", IpSource: "trusted-proxy-chain")],
            "alice", "bob", VerifiedNetworkOptions);
        Assert.DoesNotContain(result.Signals, value => value.Code == "same-city");
        Assert.False(result.NeedsDeeperAnalysis);
    }

    [Fact]
    public void DefaultPilotRetainsNetworkEvidenceWithoutUsingItForPriorityOrSkippingBaseline()
    {
        var observations = new InvestigationNetworkObservation[]
        {
            new("alice", "192.0.2.1", Now, "Example City", Country: "US", Region: "IL", IpSource: "trusted-proxy-chain"),
            new("bob", "192.0.2.1", Now.AddSeconds(30), "Example City", Country: "US", Region: "IL", IpSource: "trusted-proxy-chain")
        };
        var early = InvestigationAssessment.Assess(1, 1, observations, "alice", "bob", Options);
        Assert.Equal(0, early.ReviewPriority);
        Assert.False(early.NeedsDeeperAnalysis);
        Assert.DoesNotContain(early.Signals, value => value.Code is "shared-ip-overlap" or "same-city");
        Assert.Contains(early.Signals, value => value.Code == "network-priority-disabled");

        var baseline = InvestigationAssessment.Assess(20, 10, observations, "alice", "bob", Options);
        Assert.Equal(0, baseline.ReviewPriority);
        Assert.True(baseline.NeedsDeeperAnalysis);
    }

    [Fact]
    public void ExplicitNetworkPriorityIgnoresDirectOrUntrustedIpSources()
    {
        var result = InvestigationAssessment.Assess(1, 1,
            [new("alice", "192.0.2.1", Now, "Example City", Country: "US", Region: "IL", IpSource: "connection"),
             new("bob", "192.0.2.1", Now.AddSeconds(30), "Example City", Country: "US", Region: "IL", IpSource: "untrusted-forwarding-ignored")],
            "alice", "bob", VerifiedNetworkOptions);
        Assert.Equal(0, result.ReviewPriority);
        Assert.False(result.NeedsDeeperAnalysis);
        Assert.DoesNotContain(result.Signals, value => value.Code is "shared-ip-overlap" or "same-city");
    }

    [Fact]
    public void NetworkPriorityOptInRequiresStrictTrustedProxyRequestConfiguration()
    {
        IConfiguration Build(params (string Key, string Value)[] values) => new ConfigurationBuilder()
            .AddInMemoryCollection(values.ToDictionary(value => value.Key, value => (string?)value.Value)).Build();
        Assert.False(SecurityAuditInvestigationConfiguration.VerifiedNetworkPriorityConfigured(Build()));
        Assert.False(SecurityAuditInvestigationConfiguration.VerifiedNetworkPriorityConfigured(Build(
            ("GameSecurityRequests:IpMode", "TrustedProxyChain"),
            ("GameSecurityRequests:TrustedProxyNetworks:0", "10.0.0.0/24"))));
        Assert.True(SecurityAuditInvestigationConfiguration.VerifiedNetworkPriorityConfigured(Build(
            ("GameSecurityRequests:IpMode", "TrustedProxyChain"),
            ("GameSecurityRequests:TrustedProxyNetworks:0", "10.0.0.0/24"),
            ("GameSecurityRequests:RequireTrustedProxy", "true"))));
    }

    [Fact]
    public void PairAndInstanceIdentityIsCanonicalButDoesNotMergeDifferentGameInstances()
    {
        Assert.Equal(InvestigationDocuments.PairId("texas-holdem", "alice", "bob"), InvestigationDocuments.PairId("texas-holdem", "bob", "alice"));
        Assert.NotEqual(InvestigationDocuments.InstanceKey("texas-holdem", "table-hand-1"), InvestigationDocuments.InstanceKey("texas-holdem", "table-hand-2"));
        Assert.NotEqual(InvestigationDocuments.InstanceKey("texas-holdem", "round-1"), InvestigationDocuments.InstanceKey("blackjack", "round-1"));
    }

    [Fact]
    public void ManifestRetainsVersionIdentifiersWithoutPromotingPrivateCardsToMetadata()
    {
        var versions = InvestigationAssessment.Versions("""{"dealSeed":913,"holeCards":["As","Kh"],"engineVersion":"2","data":{"payTableId":"p1","rulesVersion":4}}""", "holdem-hand-v1", "auditor-2");
        Assert.Equal("2", versions["engineVersion"]);
        Assert.Equal("p1", versions["data.payTableId"]);
        Assert.Equal("4", versions["data.rulesVersion"]);
        Assert.DoesNotContain("dealSeed", versions.Keys);
        Assert.DoesNotContain("holeCards", versions.Keys);
    }

    [Fact]
    public void OpposedOutcomeFeatureRequiresActualOppositeSignsAndIsOnlyAProxy()
    {
        var a = new RoundAuditParticipant("alice", "Alice", true, 100, 0, -100);
        var b = new RoundAuditParticipant("bob", "Bob", true, 100, 200, 100);
        Assert.True(InvestigationAssessment.OpposedNet(a, b));
        Assert.False(InvestigationAssessment.OpposedNet(a, b with { NetCents = 0 }));
        Assert.False(InvestigationAssessment.OpposedNet(a, b with { NetCents = -20 }));
    }

    [Fact]
    public void TimestampedTimelineReconstructsFacedWagerAndHeadsUpOpponentWithoutTreatingTimeoutAsAccountChoice()
    {
        var features = InvestigationStrategyExtractor.Extract("""
        {"players":[{"actorId":"alice","isAccountBacked":true},{"actorId":"bob","isAccountBacked":true},{"actorId":"carol","isAccountBacked":true}],
         "events":[
           {"actorId":"alice","action":"small-blind","street":"preflop","amountCents":50,"committedRoundAfter":50,"currentBetAfter":50},
           {"actorId":"bob","action":"big-blind","street":"preflop","amountCents":100,"committedRoundAfter":100,"currentBetAfter":100},
           {"actorId":"carol","action":"fold","street":"preflop","amountCents":0,"committedRoundAfter":0,"currentBetAfter":100,"source":"account-action","occurredAtUtc":"2026-10-26T20:00:01Z"},
           {"actorId":"alice","action":"raise","street":"preflop","amountCents":150,"committedRoundAfter":200,"currentBetAfter":200,"stackAfter":800,"committedHandAfter":200,"source":"account-action","occurredAtUtc":"2026-10-26T20:00:02Z"},
           {"actorId":"bob","action":"call","street":"preflop","amountCents":100,"committedRoundAfter":200,"currentBetAfter":200,"source":"account-action","occurredAtUtc":"2026-10-26T20:00:03Z"},
           {"actorId":"bob","action":"fold","street":"flop","amountCents":0,"committedRoundAfter":0,"currentBetAfter":0,"source":"server-timeout","occurredAtUtc":"2026-10-26T20:00:10Z"}]}
        """);
        var raise = Assert.Single(features.Decisions, value => value.ActorId == "alice");
        Assert.True(raise.HeadsUp); Assert.Equal("bob", raise.OpponentId); Assert.Equal(50, raise.FacedWagerCents);
        Assert.Equal(800, raise.StackAfter); Assert.Equal(200, raise.CommitmentAfter); Assert.True(raise.Timestamped);
        Assert.Equal(100, Assert.Single(features.Decisions, value => value.Action == "call").FacedWagerCents);
        Assert.Equal("server-timeout", Assert.Single(features.Decisions, value => value.Street == "flop").Source);
        Assert.Equal("descriptive-only", features.Coverage);
        Assert.Equal("timeline-unavailable", InvestigationStrategyExtractor.Extract("""{"players":[],"actions":[]}""").Coverage);
    }
}
