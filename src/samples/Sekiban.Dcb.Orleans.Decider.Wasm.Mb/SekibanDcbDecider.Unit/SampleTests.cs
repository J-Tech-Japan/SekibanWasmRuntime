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
    private static long _eventCounter;

    [Test]
    public void ClassRoomProjector_Should_Preserve_MaxStudents_When_Dropping_From_Full_ClassRoom()
    {
        var classRoomId = Guid.NewGuid();
        var student1 = Guid.NewGuid();
        var student2 = Guid.NewGuid();

        ITagStatePayload state = new EmptyTagStatePayload();
        state = ClassRoomProjector.Project(state, BuildEvent(new ClassRoomCreated(classRoomId, "A", 2)));
        state = ClassRoomProjector.Project(state, BuildEvent(new StudentEnrolledInClassRoom(student1, classRoomId)));
        state = ClassRoomProjector.Project(state, BuildEvent(new StudentEnrolledInClassRoom(student2, classRoomId)));

        var filled = state as FilledClassRoomState;
        Assert.That(filled, Is.Not.Null);
        Assert.That(filled!.MaxStudents, Is.EqualTo(2));
        Assert.That(filled.IsFull, Is.True);

        state = ClassRoomProjector.Project(state, BuildEvent(new StudentDroppedFromClassRoom(student1, classRoomId)));

        var available = state as AvailableClassRoomState;
        Assert.That(available, Is.Not.Null);
        Assert.That(available!.MaxStudents, Is.EqualTo(2));
        Assert.That(available.EnrolledStudentIds, Is.EquivalentTo(new[] { student2 }));
        Assert.That(available.GetRemaining(), Is.EqualTo(1));
    }

    private static Guid CreateDeterministicGuid(long seed)
    {
        var bytes = new byte[16];
        BitConverter.GetBytes(seed).CopyTo(bytes, 0);
        return new Guid(bytes);
    }

    private static Event BuildEvent(IEventPayload payload)
    {
        var sequence = Interlocked.Increment(ref _eventCounter) - 1;
        var timestamp = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(sequence);

        return new(
            payload,
            SortableUniqueId.Generate(timestamp, CreateDeterministicGuid(sequence)),
            payload.GetType().Name,
            CreateDeterministicGuid(sequence + 1),
            new EventMetadata(CreateDeterministicGuid(sequence + 2).ToString("N"), payload.GetType().Name, "test"),
            []);
    }
}
