namespace RestLib;

/// <summary>
/// Represents the CRUD operations that RestLib can generate.
/// </summary>
public enum RestLibOperation
{
  /// <summary>Get all entities (paginated).</summary>
  GetAll,

  /// <summary>Get a single entity by ID.</summary>
  GetById,

  /// <summary>Create a new entity.</summary>
  Create,

  /// <summary>Update an existing entity (full replacement).</summary>
  Update,

  /// <summary>Partially update an existing entity.</summary>
  Patch,

  /// <summary>Delete an entity.</summary>
  Delete,

  /// <summary>Batch create operation.</summary>
  BatchCreate,

  /// <summary>Batch update operation.</summary>
  BatchUpdate,

  /// <summary>Batch patch operation.</summary>
  BatchPatch,

  /// <summary>Batch delete operation.</summary>
  BatchDelete
}
