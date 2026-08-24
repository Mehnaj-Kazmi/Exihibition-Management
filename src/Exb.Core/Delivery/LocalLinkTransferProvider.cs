using Exb.Core.Configuration;

namespace Exb.Core.Delivery;

/// <summary>
/// Serves the pack from the exhibition system itself, on a long random token.
///
/// This is the default, and deliberately so: it works on the opening morning
/// with no third-party account, no API key and no data leaving the venue, which
/// matters when the pack is assembled from a registration list. The only thing
/// it needs is that <c>PublicBaseUrl</c> is reachable from a visitor's phone,
/// which the settings screen checks and warns about.
/// </summary>
public sealed class LocalLinkTransferProvider(ExhibitionSettings exhibition, DeliverySettings delivery) : ITransferProvider
{
    public string Name => "local";

    public bool IsConfigured => !string.IsNullOrWhiteSpace(exhibition.PublicBaseUrl);

    public Task<TransferResult> UploadAsync(TransferRequest request, CancellationToken ct = default)
    {
        if (!File.Exists(request.FilePath))
            throw new FileNotFoundException("pack not found", request.FilePath);

        string baseUrl = exhibition.PublicBaseUrl.TrimEnd('/');
        var expires = delivery.LinkExpiryDays > 0
            ? DateTime.UtcNow.AddDays(delivery.LinkExpiryDays)
            : (DateTime?)null;

        return Task.FromResult(new TransferResult(
            Provider: Name,
            Url: $"{baseUrl}/d/{request.DownloadToken}",
            ExpiresUtc: expires,
            Detail: "served by the exhibition system"));
    }
}
