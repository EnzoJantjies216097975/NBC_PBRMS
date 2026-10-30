namespace PBRMS.Domain;

/// <summary>
/// What a person may do in PBRMS. One person can hold several roles, each tied to
/// an org unit (see <see cref="RoleAssignment"/>), e.g. a Supervisor of Camera who
/// is also an Operator.
/// Source: Backend Schema Document, "role_assignments".
/// </summary>
public enum Role
{
    Producer,
    ExecutiveProducer,
    Supervisor,
    BookingOfficer,
    Operator,
    Admin,
    Management,

    /// <summary>An editor (production officer). Books APP operators directly, like a producer.</summary>
    Editor,

    /// <summary>An audio post-production operator. Confirms the APP bookings made with them.</summary>
    AppOperator,

    /// <summary>Runs TV Operations. Told about, and can see, work outside the roster.</summary>
    TvOperationsManager
}
