using System.Globalization;
using System.Buffers.Binary;
using System.Text;
using Sekiban.Dcb.Tags;
namespace Dcb.MeetingRoomModels.Tags;

public record UserMonthlyReservationTag(Guid TagId, Guid UserId, DateOnly Month, string RawContent) : IGuidTagGroup<UserMonthlyReservationTag>
{
    public bool IsConsistencyTag() => true;
    public static string TagGroupName => "UserMonthlyReservation";

    public UserMonthlyReservationTag(Guid userId, DateOnly month)
        : this(CreateTagId(userId, month), userId, month, $"{userId}_{month:yyyy-MM}") { }

    public static UserMonthlyReservationTag FromContent(string content)
    {
        if (Guid.TryParse(content, out var tagId))
        {
            return new UserMonthlyReservationTag(tagId, Guid.Empty, default, content);
        }

        var parts = content.Split('_', 2);
        if (parts.Length != 2)
        {
            throw new FormatException("Invalid UserMonthlyReservationTag content.");
        }

        var userId = Guid.Parse(parts[0]);
        var month = DateOnly.ParseExact(parts[1], "yyyy-MM", CultureInfo.InvariantCulture);
        return new UserMonthlyReservationTag(userId, month);
    }

    public Guid GetId() => TagId;
    public string GetTagContent() => RawContent;

    public static UserMonthlyReservationTag FromStartTime(Guid userId, DateTime startTime) =>
        new(userId, new DateOnly(startTime.Year, startTime.Month, 1));

    private static Guid CreateTagId(Guid userId, DateOnly month)
    {
        var input = $"{userId:N}_{month:yyyy-MM}";
        var inputBytes = Encoding.UTF8.GetBytes(input);
        var guidBytes = new byte[16];
        BinaryPrimitives.WriteUInt64BigEndian(
            guidBytes.AsSpan(0, 8),
            ComputeDeterministicHash(inputBytes, seed: 14695981039346656037UL));
        BinaryPrimitives.WriteUInt64BigEndian(
            guidBytes.AsSpan(8, 8),
            ComputeDeterministicHash(inputBytes, seed: 1099511628211UL ^ 0x9e3779b97f4a7c15UL));
        return new Guid(guidBytes);
    }

    private static ulong ComputeDeterministicHash(ReadOnlySpan<byte> input, ulong seed)
    {
        const ulong prime = 1099511628211UL;
        var hash = seed;
        foreach (var value in input)
        {
            hash ^= value;
            hash *= prime;
        }

        return hash;
    }
}
