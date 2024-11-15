namespace Xtkl.NceTransferWebhooks.DTOs
{
    record TransferWebhookDto(string EventName, string ResourceUri, string ResourceName, string AuditUri, DateTime ResourceChangeUtcDate);
}
