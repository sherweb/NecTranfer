enum TenantRegion
{
    US, // United States
    CA, // Canada
    EU  // Europe
}

enum TransferStatus
{
    Complete,
    Expired
}

enum TransferType
{
    NewCommerce = 3
}

enum TransferDirection
{
    IncomingTransfer = 1,
    OutgoingTransfer = 2,
}

public enum TransferEventType
{
    CompleteTransfer,
    FailTransfer,
    UpdateTransfer
}

public static class TransferEventTypeExtensions
{
    public static string ToTransferEventString(this TransferEventType eventType)
    {
        switch (eventType)
        {
            case TransferEventType.CompleteTransfer:
                return "complete-transfer";
            case TransferEventType.UpdateTransfer:
                return "update-transfer";
            case TransferEventType.FailTransfer:
                return "fail-transfer";
            default:
                throw new ArgumentOutOfRangeException();
        }
    }
}