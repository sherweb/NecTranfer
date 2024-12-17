using Xtkl.Apps.Legacy.Services.Contracts.AdminPortal.Tools.MicrosoftTransferIn;

namespace Xtkl.NceTransferWebhooks.Model
{
    record Transfer(
        string id,
        string status,
        int transferType,
        string customerEmailId,
        DateTime createdTime,
        DateTime LastModifiedTime,
        DateTime? completedTime,
        DateTime? expirationTime,
        string customerName,
        string customerTenantId,
        string partnerTenantId,
        string sourcePartnerName,
        string sourcePartnerTenantId,
        string targetPartnerName,
        string targetPartnerTenantId,
        string targetPartnerEmailId,
        int transferDirection,
        bool ignoreEligibilityCheck,
        string lastModifiedUser,
        string importResult,
        TransferInSubscription[] successfulSubscriptions,
        TransferInSubscription[] notTransferredSubscriptions
    );

}
