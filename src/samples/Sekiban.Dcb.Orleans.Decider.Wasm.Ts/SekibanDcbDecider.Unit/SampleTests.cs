using NUnit.Framework;
using Dcb.EventSource.ClassRoom;
using Dcb.ImmutableModels.Events.ClassRoom;
using Dcb.ImmutableModels.Events.Enrollment;
using Dcb.ImmutableModels.States.ClassRoom;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.Tags;

namespace SekibanDcbOrleans.Unit;

public class SampleTests
{
    private static readonly DateTime FixedTimestamp = new(2026, 4, 7, 12, 0, 0, DateTimeKind.Utc);
    private static readonly Guid FixedCommitId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid FixedClassRoomId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid FixedStudent1 = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid FixedStudent2 = Guid.Parse("44444444-4444-4444-4444-444444444444");

    [Test]
    public void ClassRoomProjector_Should_Preserve_MaxStudents_When_Dropping_From_Full_ClassRoom()
    {
        ITagStatePayload state = new EmptyTagStatePayload();
        state = ClassRoomProjector.Project(state, BuildEvent(new ClassRoomCreated(FixedClassRoomId, "A", 2)));
        state = ClassRoomProjector.Project(state, BuildEvent(new StudentEnrolledInClassRoom(FixedStudent1, FixedClassRoomId)));
        state = ClassRoomProjector.Project(state, BuildEvent(new StudentEnrolledInClassRoom(FixedStudent2, FixedClassRoomId)));

        var filled = state as FilledClassRoomState;
        Assert.That(filled, Is.Not.Null);
        Assert.That(filled!.MaxStudents, Is.EqualTo(2));
        Assert.That(filled.IsFull, Is.True);

        state = ClassRoomProjector.Project(state, BuildEvent(new StudentDroppedFromClassRoom(FixedStudent1, FixedClassRoomId)));

        var available = state as AvailableClassRoomState;
        Assert.That(available, Is.Not.Null);
        Assert.That(available!.MaxStudents, Is.EqualTo(2));
        Assert.That(available.EnrolledStudentIds, Is.EquivalentTo(new[] { FixedStudent2 }));
        Assert.That(available.GetRemaining(), Is.EqualTo(1));
    }

    private static Event BuildEvent(IEventPayload payload) =>
        new(
            payload,
            SortableUniqueId.Generate(FixedTimestamp, FixedCommitId),
            payload.GetType().Name,
            FixedCommitId,
            new EventMetadata(FixedCommitId.ToString("N"), payload.GetType().Name, "test"),
            []);
}
