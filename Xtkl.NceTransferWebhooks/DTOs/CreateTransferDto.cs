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
        [SwaggerSchema("A string ID that identifies a unique identifier assigned to a company that is a partner with Microsoft.")]
        public string MpnId { get; init; }

        [JsonRequired]
        [SwaggerSchema("A GUID formatted partner ID that identifies the losing partner initiating the transfer.")]
        public Guid LosingPartnerId { get; init; }

        [JsonRequired]
        [SwaggerSchema("The name of the losing partner initiating the transfer.")]
        public string LosingPartnerName { get; init; }

        [JsonRequired]
        [SwaggerSchema("The email of the customer to receive notifications of the transfer creation.")]
        public string CustomerEmail { get; init; }

        [JsonRequired]
        [SwaggerSchema("The name of the customer whose subscriptions are being transferred.")]
        public string CustomerName { get; init; }

        [JsonRequired]
        [SwaggerSchema("A organization unique name in Cumulus.")]
        public string CumulusOrganizationUniqueName { get; init; }
    }
}