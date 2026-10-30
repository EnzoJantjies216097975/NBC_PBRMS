using PBRMS.Domain;

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

    /// <summary>409 with the clashes listed, so the app can show exactly which productions are in the way.</summary>
    public static IResult Clashes(string why, List<Clash> clashes) => Results.Problem(
        why,
        statusCode: StatusCodes.Status409Conflict,
        extensions: new Dictionary<string, object?> { ["clashes"] = clashes });

    /// <summary>
    /// 409 with warnings that someone must acknowledge before this can go ahead. Nothing was
    /// saved. The app shows them, and sends the request again with acknowledgeWarnings: true.
    /// </summary>
    public static IResult Warnings(string why, List<Clash> warnings) => Results.Problem(
        why,
        statusCode: StatusCodes.Status409Conflict,
        extensions: new Dictionary<string, object?> { ["clashes"] = warnings, ["needsAcknowledgement"] = true });

    /// <summary>400: something in the request needs fixing; says which field and why.</summary>
    public static IResult Invalid(string field, string why) => Results.ValidationProblem(new Dictionary<string, string[]> { [field] = [why] });
}
