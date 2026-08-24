using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Exb.Core.Configuration;

namespace Exb.Core.Delivery;

/// <summary>
/// Uploads the pack to WeTransfer through their public v2 API.
///
/// The flow is: authorise with the API key for a bearer token, declare the
/// transfer and its files, then for each part ask for a pre-signed URL and PUT
/// the bytes straight to storage, complete the file, and finalise the transfer
/// to get the shareable link. Chunk size comes back from the API rather than
/// being assumed, because it is theirs to change.
///
/// Note this has been written against the documented API but not exercised
/// against a live account, since that needs a WeTransfer API key the venue must
/// supply. Until one is configured the provider reports itself unconfigured and
/// delivery falls back to the local link provider rather than failing at 19:00
/// on the first evening.
/// </summary>
public sealed class WeTransferProvider(WeTransferSettings settings, IHttpClientFactory httpClientFactory) : ITransferProvider
{
    public string Name => "wetransfer";

    public bool IsConfigured => !string.IsNullOrWhiteSpace(settings.ApiKey);

    public async Task<TransferResult> UploadAsync(TransferRequest request, CancellationToken ct = default)
    {
        if (!IsConfigured)
            throw new InvalidOperationException("WeTransfer API key is not configured.");

        var file = new FileInfo(request.FilePath);
        if (!file.Exists) throw new FileNotFoundException("pack not found", request.FilePath);

        using var http = httpClientFactory.CreateClient("wetransfer");
        http.Timeout = TimeSpan.FromMinutes(30);
        string baseUrl = settings.BaseUrl.TrimEnd('/');

        // 1. Authorise.
        using var authRequest = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/authorize");
        authRequest.Headers.Add("x-api-key", settings.ApiKey);
        using var authResponse = await http.SendAsync(authRequest, ct);
        string token = (await ReadJsonAsync(authResponse, ct)).GetProperty("token").GetString()
            ?? throw new InvalidOperationException("WeTransfer did not return a token.");

        http.DefaultRequestHeaders.Add("x-api-key", settings.ApiKey);
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // 2. Declare the transfer.
        string fileName = Path.GetFileName(request.FilePath);
        using var createResponse = await http.PostAsJsonAsync($"{baseUrl}/transfers", new
        {
            message = request.Message ?? settings.Message,
            files = new[] { new { name = fileName, size = file.Length } },
        }, ct);

        var transfer = await ReadJsonAsync(createResponse, ct);
        string transferId = transfer.GetProperty("id").GetString()!;
        var fileEntry = transfer.GetProperty("files")[0];
        string fileId = fileEntry.GetProperty("id").GetString()!;
        var multipart = fileEntry.GetProperty("multipart");
        int partCount = multipart.GetProperty("part_numbers").GetInt32();
        long chunkSize = multipart.GetProperty("chunk_size").GetInt64();

        // 3. Upload each part to the pre-signed URL it is given.
        await using (var source = File.OpenRead(request.FilePath))
        {
            var buffer = new byte[chunkSize];
            for (int part = 1; part <= partCount; part++)
            {
                int read = await ReadExactlyAsync(source, buffer, ct);
                if (read == 0) break;

                using var urlResponse = await http.GetAsync(
                    $"{baseUrl}/transfers/{transferId}/files/{fileId}/upload-url/{part}", ct);
                string uploadUrl = (await ReadJsonAsync(urlResponse, ct)).GetProperty("url").GetString()!;

                using var content = new ByteArrayContent(buffer, 0, read);
                content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");

                // The pre-signed URL carries its own auth; sending ours would be rejected.
                using var plain = httpClientFactory.CreateClient("wetransfer-upload");
                plain.Timeout = TimeSpan.FromMinutes(30);
                using var putResponse = await plain.PutAsync(uploadUrl, content, ct);
                putResponse.EnsureSuccessStatusCode();
            }
        }

        // 4. Complete the file, then finalise the transfer.
        using var completeResponse = await http.PutAsJsonAsync(
            $"{baseUrl}/transfers/{transferId}/files/{fileId}/upload-complete",
            new { part_numbers = partCount }, ct);
        completeResponse.EnsureSuccessStatusCode();

        using var finalizeResponse = await http.PutAsync($"{baseUrl}/transfers/{transferId}/finalize", null, ct);
        var finalized = await ReadJsonAsync(finalizeResponse, ct);
        string url = finalized.GetProperty("url").GetString()
            ?? throw new InvalidOperationException("WeTransfer did not return a download URL.");

        return new TransferResult(Name, url, DateTime.UtcNow.AddDays(7), $"transfer {transferId}");
    }

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response, CancellationToken ct)
    {
        string body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"WeTransfer returned {(int)response.StatusCode}: {Truncate(body)}");

        using var document = JsonDocument.Parse(body);
        return document.RootElement.Clone();
    }

    private static async Task<int> ReadExactlyAsync(Stream stream, byte[] buffer, CancellationToken ct)
    {
        int total = 0;
        while (total < buffer.Length)
        {
            int read = await stream.ReadAsync(buffer.AsMemory(total), ct);
            if (read == 0) break;
            total += read;
        }
        return total;
    }

    private static string Truncate(string s) => s.Length <= 400 ? s : s[..400] + "...";
}
