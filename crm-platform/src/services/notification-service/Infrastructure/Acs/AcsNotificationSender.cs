using CrmPlatform.NotificationService.Domain.Entities;
using CrmPlatform.NotificationService.Domain.Enums;
using Azure.Communication.Email;
using Azure.Communication.Sms;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Azure.NotificationHubs;

namespace CrmPlatform.NotificationService.Infrastructure.Acs;

// ──────────────────────────────────────────────────────────────────────────────
// Options
// ──────────────────────────────────────────────────────────────────────────────
public sealed class AcsOptions
{
    public string EmailConnectionString      { get; init; } = string.Empty;
    public string SmsConnectionString        { get; init; } = string.Empty;
    public string NotificationHubConnectionString { get; init; } = string.Empty;
    public string NotificationHubName        { get; init; } = string.Empty;
    public string EmailSenderAddress         { get; init; } = string.Empty;
    public string AcsWebhookHmacSecret       { get; init; } = string.Empty;  // from Key Vault
}

// ──────────────────────────────────────────────────────────────────────────────
// Abstraction
// ──────────────────────────────────────────────────────────────────────────────
public interface INotificationSender
{
    /// <summary>
    /// Sends a rendered notification via the appropriate ACS channel.
    /// Returns the provider-assigned message ID.
    /// </summary>
    Task<string> SendAsync(NotificationRecord record, CancellationToken ct = default);
}

// ──────────────────────────────────────────────────────────────────────────────
// Implementation
// ──────────────────────────────────────────────────────────────────────────────
public sealed class AcsNotificationSender(
    IOptions<AcsOptions> options,
    ILogger<AcsNotificationSender> logger)
    : INotificationSender
{
    private readonly AcsOptions _opts = options.Value;

    public Task<string> SendAsync(NotificationRecord record, CancellationToken ct = default)
        => record.Channel switch
        {
            NotificationChannel.Email      => SendEmailAsync(record, ct),
            NotificationChannel.Sms        => SendSmsAsync(record, ct),
            NotificationChannel.WebPush    => SendPushAsync(record, ct),
            NotificationChannel.MobilePush => SendPushAsync(record, ct),
            NotificationChannel.InApp      => Task.FromResult(Guid.NewGuid().ToString()),
            _ => throw new NotSupportedException($"Channel {record.Channel} is not supported.")
        };

    // ─── Email ────────────────────────────────────────────────────────────────
    private async Task<string> SendEmailAsync(NotificationRecord record, CancellationToken ct)
    {
        var client = new EmailClient(_opts.EmailConnectionString);

        var message = new EmailMessage(
            senderAddress: _opts.EmailSenderAddress,
            content: new EmailContent(record.Subject ?? "(no subject)")
            {
                PlainText = record.BodyPlain,
                Html      = record.BodyHtml
            },
            recipients: new EmailRecipients(
            [
                new EmailAddress(record.RecipientAddress)
            ]));

        var operation = await client.SendAsync(
            Azure.WaitUntil.Started, message, ct);

        logger.LogInformation(
            "ACS Email queued. OperationId={OperationId} RecipientUserId={UserId}",
            operation.Id, record.RecipientUserId);

        return operation.Id;
    }

    // ─── SMS ──────────────────────────────────────────────────────────────────
    private async Task<string> SendSmsAsync(NotificationRecord record, CancellationToken ct)
    {
        var client = new SmsClient(_opts.SmsConnectionString);

        var response = await client.SendAsync(
            from: _opts.EmailSenderAddress,  // reuse ACS phone number from config
            to:   record.RecipientAddress,
            message: record.BodyPlain,
            cancellationToken: ct);

        var msgId = response.Value.MessageId ?? Guid.NewGuid().ToString();

        logger.LogInformation(
            "ACS SMS sent. MessageId={MessageId} RecipientUserId={UserId}",
            msgId, record.RecipientUserId);

        return msgId;
    }

    // ─── Push (Web + Mobile) ──────────────────────────────────────────────────
    private async Task<string> SendPushAsync(NotificationRecord record, CancellationToken ct)
    {
        var client = NotificationHubClient.CreateClientFromConnectionString(
            _opts.NotificationHubConnectionString,
            _opts.NotificationHubName);

        // RecipientAddress is the device registration tag (userId:<guid>)
        var notification = $$"""
            {
              "message": {
                "notification": {
                  "title": "{{EscapeJson(record.Subject ?? "Notification")}}",
                  "body":  "{{EscapeJson(record.BodyPlain)}}"
                }
              }
            }
            """;

        var result = await client.SendNotificationAsync(
            new FcmNotification(notification),
            record.RecipientAddress);

        var trackingId = result?.TrackingId ?? Guid.NewGuid().ToString();

        logger.LogInformation(
            "ACS Push sent. TrackingId={TrackingId} Tag={Tag} RecipientUserId={UserId}",
            trackingId, record.RecipientAddress, record.RecipientUserId);

        return trackingId;
    }

    private static string EscapeJson(string s)
        => s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n");
}
