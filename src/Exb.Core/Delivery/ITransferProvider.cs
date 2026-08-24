namespace Exb.Core.Delivery;

public sealed record TransferRequest(
    string FilePath,
    string DisplayName,
    string DownloadToken,
    string RecipientName,
    string? Message);

public sealed record TransferResult(
    string Provider,
    string Url,
    DateTime? ExpiresUtc,
    string? Detail);

/// <summary>
/// Somewhere to put a finished e-catalogue pack so a link can be emailed.
///
/// This is an interface rather than a WeTransfer call because organisers keep
/// changing their mind about it: some have a corporate file-transfer gateway,
/// some want nothing to leave the venue at all, and some genuinely do want
/// WeTransfer. Swapping provider is a settings change, not a code change.
/// </summary>
public interface ITransferProvider
{
    string Name { get; }

    /// <summary>False when the provider has not been configured with the credentials it needs.</summary>
    bool IsConfigured { get; }

    Task<TransferResult> UploadAsync(TransferRequest request, CancellationToken ct = default);
}
