using Dcb.ImmutableModels.Events.Enrollment;
using Dcb.ImmutableModels.States.Student;
using Dcb.ImmutableModels.States.Student.Deciders;

namespace SekibanDcbOrleans.ImmutableModels.Unit;

public class StudentEnrolledInClassRoomDeciderTests
{
    [Fact]
    public void Validate_AllowsEnrollment_WhenRemainingSlotsExist()
    {
        var state = new StudentState(Guid.NewGuid(), "Alice", 2, [Guid.NewGuid()]);

        state.Validate(Guid.NewGuid());
    }

    [Fact]
    public void Validate_Throws_WhenRemainingSlotsAreZero()
    {
        var state = new StudentState(Guid.NewGuid(), "Alice", 1, [Guid.NewGuid()]);

        Assert.Throws<InvalidOperationException>(() => state.Validate(Guid.NewGuid()));
    }

    [Fact]
    public void Validate_Throws_WhenAlreadyEnrolled()
    {
        var classRoomId = Guid.NewGuid();
        var state = new StudentState(Guid.NewGuid(), "Alice", 2, [classRoomId]);

        Assert.Throws<InvalidOperationException>(() => state.Validate(classRoomId));
    }

    [Fact]
    public void Evolve_IsIdempotent_WhenStudentIsAlreadyEnrolled()
    {
        var studentId = Guid.NewGuid();
        var classRoomId = Guid.NewGuid();
        var state = new StudentState(studentId, "Alice", 2, [classRoomId]);

        var evolved = state.Evolve(new StudentEnrolledInClassRoom(studentId, classRoomId));

        Assert.Equal(state, evolved);
    }
}
