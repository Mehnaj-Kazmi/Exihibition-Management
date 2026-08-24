using System.Net.Http.Headers;
using System.Text.Json;
using Exb.Core.Configuration;

namespace Exb.Core.Delivery;

/// <summary>
/// Posts the pack as a multipart upload to any endpoint that answers with a
/// JSON body containing a download URL.
///
/// This exists because in practice most venues already have something — a
/// corporate file-transfer gateway, an S3 pre-signed upload service, Filemail,
/// an in-house drop — and none of them share an API. Rather than write an
/// adapter per service, this covers all of them with two settings: where to POST
/// and where in the response the URL lives.
/// </summary>
public sealed class GenericHttpTransferProvider(GenericTransferSettings settings, IHttpClientFactory httpClientFactory) : ITransferProvider
{
    public string Name => "generic";

    public bool IsConfigured => !string.IsNullOrWhiteSpace(settings.UploadUrl);

    public async Task<TransferResult> UploadAsync(TransferRequest request, CancellationToken ct = default)
    {
        if (!IsConfigured)
            throw new InvalidOperationException("Generic transfer upload URL is not configured.");
        if (!File.Exists(request.FilePath))
            throw new FileNotFoundException("pack not found", request.FilePath);

        using var http = httpClientFactory.CreateClient("transfer-generic");
        http.Timeout = TimeSpan.FromMinutes(30);

        using var form = new MultipartFormDataContent();
        await using var stream = File.OpenRead(request.FilePath);
        var fileContent = new StreamContent(stream);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/zip");
        form.Add(fileContent, settings.FileFieldName, Path.GetFileName(request.FilePath));
        form.Add(new StringContent(request.DisplayName), "name");
        if (!string.IsNullOrWhiteSpace(request.Message))
            form.Add(new StringContent(request.Message), "message");

        using var message = new HttpRequestMessage(HttpMethod.Post, settings.UploadUrl) { Content = form };
        if (!string.IsNullOrWhiteSpace(settings.AuthorizationHeader))
            message.Headers.TryAddWithoutValidation("Authorization", settings.AuthorizationHeader);

        using var response = await http.SendAsync(message, ct);
        string body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"upload endpoint returned {(int)response.StatusCode}: {Truncate(body)}");

        string url = ExtractUrl(body, settings.UrlJsonPath)
            ?? throw new InvalidOperationException(
                $"no download URL at '{settings.UrlJsonPath}' in the upload response: {Truncate(body)}");

        return new TransferResult(Name, url, null, "generic upload endpoint");
    }

    /// <summary>Follow a dotted path such as "data.links.download" into the JSON response.</summary>
    internal static string? ExtractUrl(string json, string path)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var element = document.RootElement;

            foreach (string segment in path.Split('.', StringSplitOptions.RemoveEmptyEntries))
            {
                if (element.ValueKind == JsonValueKind.Array && int.TryParse(segment, out int index))
                {
                    if (index >= element.GetArrayLength()) return null;
                    element = element[index];
                }
                else if (element.ValueKind == JsonValueKind.Object && element.TryGetProperty(segment, out var next))
                {
                    element = next;
                }
                else return null;
            }

            return element.ValueKind == JsonValueKind.String ? element.GetString() : null;
        }
        catch (JsonException)
        {
            // Some gateways answer with a bare URL and no JSON at all.
            string trimmed = json.Trim();
            return trimmed.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? trimmed : null;
        }
    }

    private static string Truncate(string s) => s.Length <= 400 ? s : s[..400] + "...";
}
