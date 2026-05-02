namespace CrmPlatform.AnalyticsService.Domain.Enums;

/// <summary>Source service that produced the event being tracked.</summary>
public enum EventSource
{
    Sfa        = 0,
    Css        = 1,
    Marketing  = 2,
    Identity   = 3,
    Platform   = 4
}

/// <summary>
/// Granularity of a pre-aggregated metric snapshot.
/// Daily snapshots are materialised by the rollup background service.
/// </summary>
public enum MetricGranularity
{
    Hourly  = 0,
    Daily   = 1,
    Weekly  = 2,
    Monthly = 3
}

/// <summary>Named dashboard widget types.</summary>
public enum MetricKey
{
    // SFA
    LeadsCreated        = 100,
    LeadsConverted      = 101,
    OpportunitiesWon    = 102,
    OpportunitiesLost   = 103,
    RevenueWon          = 104,

    // CSS
    CasesCreated        = 200,
    CasesResolved       = 201,
    CasesClosed         = 202,
    SlaBreaches         = 203,
    MeanTimeToResolve   = 204,   // minutes

    // Marketing
    JourneysCompleted   = 300,
    CampaignsActivated  = 301,
    EnrollmentsCreated  = 302,

    // Platform
    TenantsProvisioned  = 400
}
