using Exb.Core.Configuration;
using Exb.Core.Delivery;
using Exb.Core.Mail;
using Exb.Data.Services;

namespace Exb.Web.Services;

/// <summary>
/// Picks the transfer provider named in settings, and falls back to serving the
/// pack from this system when the named one has not been given its credentials.
///
/// The fallback is deliberate. The alternative is that an organiser selects
/// WeTransfer during setup, forgets the API key, and discovers it at seven in
/// the evening when three hundred packs fail at once. A pack that arrives on a
/// local link is a far better outcome than a pack that does not arrive.
/// </summary>
public sealed class TransferProviderSelector(
    IHttpClientFactory httpClientFactory,
    ILogger<TransferProviderSelector> logger) : ITransferProviderSelector
{
    public ITransferProvider Resolve(AppSettings settings)
    {
        var local = new LocalLinkTransferProvider(settings.Exhibition, settings.Delivery);

        ITransferProvider chosen = settings.Delivery.Provider?.ToLowerInvariant() switch
        {
            "wetransfer" => new WeTransferProvider(settings.Delivery.WeTransfer, httpClientFactory),
            "generic" => new GenericHttpTransferProvider(settings.Delivery.Generic, httpClientFactory),
            _ => local,
        };

        if (chosen.IsConfigured) return chosen;

        if (chosen.Name != local.Name)
            logger.LogWarning(
                "Delivery provider '{Provider}' is selected but not configured; packs will be served from this system instead.",
                chosen.Name);

        return local;
    }
}

/// <summary>Resolves the mail transport named in settings, at the moment of sending.</summary>
public interface IMailTransportSelector
{
    IMailTransport Resolve(MailSettings settings);
}

public sealed class MailTransportSelector : IMailTransportSelector
{
    public IMailTransport Resolve(MailSettings settings)
        => settings.Provider?.Equals("smtp", StringComparison.OrdinalIgnoreCase) == true
            ? new SmtpMailTransport(settings)
            : new HeldMailTransport();
}
