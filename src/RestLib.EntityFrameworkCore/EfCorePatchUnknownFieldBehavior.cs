namespace RestLib.EntityFrameworkCore;

/// <summary>
/// Controls how PATCH operations handle unknown or forbidden fields.
/// </summary>
public enum EfCorePatchUnknownFieldBehavior
{
    /// <summary>
    /// Ignores unknown fields and primary key fields. This is the default behavior.
    /// </summary>
    Permissive,

    /// <summary>
    /// Rejects unknown fields and primary key fields with a bad request error.
    /// </summary>
    Strict,
}
