namespace RestLib.EntityFrameworkCore;

/// <summary>
/// Represents the type of database constraint that was violated.
/// </summary>
public enum EfCoreConstraintType
{
    /// <summary>
    /// A unique or primary key constraint was violated.
    /// </summary>
    UniqueConstraint,

    /// <summary>
    /// A foreign key constraint was violated.
    /// </summary>
    ForeignKeyConstraint,

    /// <summary>
    /// The constraint type could not be determined.
    /// </summary>
    Unknown,
}
