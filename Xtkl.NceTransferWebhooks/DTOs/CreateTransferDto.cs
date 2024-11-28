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
        [SwaggerSchema("A GUID formatted tenant ID that identifies the customer.")]
        public Guid TenantId { get; init; }

        [JsonRequired]
        [SwaggerSchema("A GUID formatted partner ID that identifies the partner initiating the transfer.")]
        public Guid PartnerId { get; init; }

        [JsonRequired]
        [SwaggerSchema("The name of the partner's organization initiating the transfer.")]
        public string PartnerName { get; init; }

        [JsonRequired]
        [SwaggerSchema("The email of the customer to receive notifications of the transfer creation.")]
        public string CustomerEmail { get; init; }

        [JsonRequired]
        [SwaggerSchema("The name of the customer whose subscriptions are being transferred.")]
        public string CustomerName { get; init; }

        [JsonRequired]
        [SwaggerSchema("A GUID formatted Org ID that identifies the customer in CUmulus.")]
        public Guid CumulusOrgId { get; init; }
    }
}
