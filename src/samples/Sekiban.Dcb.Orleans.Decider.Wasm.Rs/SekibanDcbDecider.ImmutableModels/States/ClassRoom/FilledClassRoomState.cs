using Sekiban.Dcb.Tags;
namespace Dcb.ImmutableModels.States.ClassRoom;

public record FilledClassRoomState(Guid ClassRoomId, string Name, int MaxStudents, List<Guid> EnrolledStudentIds, bool IsFull)
    : ITagStatePayload
{
    public static FilledClassRoomState Empty => new(Guid.Empty, string.Empty, 0, [], false);
}
