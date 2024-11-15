using Swashbuckle.AspNetCore.Annotations;
using System.Text.Json.Serialization;

namespace Xtkl.NceTransferWebhooks.DTOs
{
    record CreateTransferDto
    {
        [JsonRequired]
        [SwaggerSchema("The region of the tenant, restricted to 'CA', 'US', or 'EU'.")]
        [System.Text.Json.Serialization.JsonConverter(typeof(JsonStringEnumConverter))]
        public TenantRegion TenantRegion { get; init; }

        [JsonRequired]
        [SwaggerSchema("A GUID formatted customer ID that identifies the customer.")]
        public Guid CustomerId { get; init; }

        [JsonRequired]
        [SwaggerSchema("A GUID formatted partner ID that identifies the partner initiating the transfer.")]
        public Guid SourcePartnerTenantId { get; init; }

        [JsonRequired]
        [SwaggerSchema("The name of the partner's organization initiating the transfer.")]
        public string SourcePartnerName { get; init; }

        [JsonRequired]
        [SwaggerSchema("The email ID of the customer to receive notifications of the transfer creation.")]
        public string CustomerEmailId { get; init; }

        [SwaggerSchema("The name of the customer whose subscriptions are being transferred.")]
        public string CustomerName { get; init; }

        [SwaggerSchema("A GUID formatted partner tenant ID that identifies the partner to whom the transfer is targeted.")]
        public Guid TargetPartnerTenantId { get; init; }

        [SwaggerSchema("The email ID of the partner to whom the transfer is targeted.")]
        public string TargetPartnerEmailId { get; init; }
    }
}
