namespace CrmPlatform.IntegrationService.Domain.Enums;

public enum ConnectorType
{
    Salesforce      = 1,
    HubSpot         = 2,
    GenericWebhook  = 3,
    AzureEventHub   = 4,
    AzureBlobExport = 5,
}

public enum ConnectorStatus
{
    Disconnected = 0,
    Connected    = 1,
    Error        = 2,
    Suspended    = 3,
}

public enum OutboundJobStatus
{
    Queued      = 0,
    InProgress  = 1,
    Succeeded   = 2,
    Failed      = 3,
    Abandoned   = 4,
}

public enum InboundEventStatus
{
    Received  = 0,
    Published = 1,
    Failed    = 2,
    Skipped   = 3,
}
