using System.Net.Http.Json;
using System.Text.Json;
using Sekiban.Dcb.Tags;

namespace SekibanWasm.Cs.ClientApi;

public interface ITagExistenceChecker
{
    Task<bool> ExistsAsync(ITag tag, CancellationToken ct);
}

public sealed class TagExistenceChecker(HttpClient httpClient) : ITagExistenceChecker
{
    private static readonly JsonSerializerOptions TransportJsonOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _httpClient = httpClient;

    public async Task<bool> ExistsAsync(ITag tag, CancellationToken ct)
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
        return payload?.Exists ?? false;
    }

    private sealed record TagLatestSortableRequest(string Tag);
    private sealed record TagLatestSortableResponse(bool Exists, string LastSortableUniqueId);
}
