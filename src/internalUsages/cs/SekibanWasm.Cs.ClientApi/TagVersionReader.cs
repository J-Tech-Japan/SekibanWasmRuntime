using System.Net.Http.Json;
using System.Text.Json;
using Sekiban.Dcb.Tags;

namespace SekibanWasm.Cs.ClientApi;

public sealed record TagVersion(bool Exists, string LastSortableUniqueId);

public interface ITagVersionReader
{
    Task<TagVersion> ReadAsync(ITag tag, CancellationToken ct);
}

public sealed class TagVersionReader(HttpClient httpClient) : ITagVersionReader
{
    private static readonly JsonSerializerOptions TransportJsonOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _httpClient = httpClient;

    public async Task<TagVersion> ReadAsync(ITag tag, CancellationToken ct)
    {
        var response = await _httpClient.PostAsJsonAsync(
            "/api/sekiban/serialized/tag-latest-sortable",
            new TagLatestSortableRequest(tag.GetTag()),
            TransportJsonOptions,
            ct);
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<TagLatestSortableResponse>(
            TransportJsonOptions,
            ct);
        return payload is null
            ? new TagVersion(false, string.Empty)
            : new TagVersion(payload.Exists, payload.LastSortableUniqueId ?? string.Empty);
    }

    private sealed record TagLatestSortableRequest(string Tag);
    private sealed record TagLatestSortableResponse(bool Exists, string? LastSortableUniqueId);
}
