namespace PBRMS.Api.Endpoints;

/// <summary>
/// Error responses that give the reason in plain English, in the standard
/// problem-details format, so the apps can show the reason as it is.
/// </summary>
internal static class Problems
{
    /// <summary>403: the person is signed in but may not do this.</summary>
    public static IResult Forbidden(string why) => Results.Problem(why, statusCode: StatusCodes.Status403Forbidden);

    /// <summary>409: the request is fine, but the record's current state doesn't allow it.</summary>
    public static IResult Conflict(string why) => Results.Problem(why, statusCode: StatusCodes.Status409Conflict);

    /// <summary>400: something in the request needs fixing; says which field and why.</summary>
    public static IResult Invalid(string field, string why) => Results.ValidationProblem(new Dictionary<string, string[]> { [field] = [why] });
}
