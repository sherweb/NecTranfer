namespace Xtkl.NceTransferWebhooks.DTOs
{
    public record TokenResponse(
        string access_token,
        string token_type,
        string expires_on,
        string scope,
        string refresh_token
    );
}
