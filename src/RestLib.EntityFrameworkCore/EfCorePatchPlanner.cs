using System.Reflection;
using System.Text.Json;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata;
using RestLib.Serialization;

namespace RestLib.EntityFrameworkCore;

/// <summary>
/// Represents one validated property assignment in a PATCH plan.
/// </summary>
/// <param name="PropertyInfo">The mapped CLR property.</param>
/// <param name="Value">The merged value to assign.</param>
internal sealed record EfCorePatchOperation(PropertyInfo PropertyInfo, object? Value);

/// <summary>
/// Represents a validated PATCH plan for one tracked entity.
/// </summary>
/// <typeparam name="TEntity">The entity type.</typeparam>
internal sealed class EfCorePatchPlan<TEntity>
    where TEntity : class
{
    /// <summary>
    /// Initializes a new instance of the <see cref="EfCorePatchPlan{TEntity}"/> class.
    /// </summary>
    /// <param name="entry">The tracked entry that receives the operations.</param>
    /// <param name="operations">The validated property operations.</param>
    internal EfCorePatchPlan(
        EntityEntry<TEntity> entry,
        IReadOnlyList<EfCorePatchOperation> operations)
    {
        Entry = entry;
        Operations = operations;
    }

    /// <summary>
    /// Gets the number of property operations in the plan.
    /// </summary>
    internal int OperationCount => Operations.Count;

    /// <summary>
    /// Gets the tracked entity entry that receives the operations.
    /// </summary>
    internal EntityEntry<TEntity> Entry { get; }

    /// <summary>
    /// Gets the validated property operations.
    /// </summary>
    internal IReadOnlyList<EfCorePatchOperation> Operations { get; }
}

/// <summary>
/// Captures one tracked property's value and modification state before PATCH application.
/// </summary>
/// <typeparam name="TEntity">The entity type.</typeparam>
/// <param name="Entry">The tracked entity entry.</param>
/// <param name="PropertyName">The mapped property name.</param>
/// <param name="CurrentValue">The original current value.</param>
/// <param name="IsModified">The original modification flag.</param>
internal sealed record EfCorePatchPropertySnapshot<TEntity>(
    EntityEntry<TEntity> Entry,
    string PropertyName,
    object? CurrentValue,
    bool IsModified)
    where TEntity : class;

/// <summary>
/// Builds, applies, and restores EF Core PATCH plans without mutating entities during validation.
/// </summary>
/// <typeparam name="TEntity">The entity type.</typeparam>
internal sealed class EfCorePatchPlanner<TEntity>
    where TEntity : class
{
    private readonly JsonObjectContract _jsonContract;
    private readonly IReadOnlyDictionary<string, PatchPropertyContract> _patchPropertiesByClrName;

    /// <summary>
    /// Initializes a new instance of the <see cref="EfCorePatchPlanner{TEntity}"/> class.
    /// </summary>
    /// <param name="model">The EF Core model containing the entity metadata.</param>
    /// <param name="jsonContract">The canonical JSON contract for the entity type.</param>
    internal EfCorePatchPlanner(IModel model, JsonObjectContract jsonContract)
    {
        ArgumentNullException.ThrowIfNull(model);

        _jsonContract = jsonContract ?? throw new ArgumentNullException(nameof(jsonContract));
        _patchPropertiesByClrName = BuildPatchPropertyMap(model, _jsonContract);
    }

    /// <summary>
    /// Builds a complete PATCH plan without changing tracked property values.
    /// </summary>
    /// <param name="entry">The tracked entry that will receive the plan.</param>
    /// <param name="entity">The entity whose current values are used for merge semantics.</param>
    /// <param name="patchDocument">The JSON Merge Patch document.</param>
    /// <param name="keyPropertyNames">The immutable key-property names.</param>
    /// <param name="unknownFieldBehavior">The configured unknown-field behavior.</param>
    /// <param name="plannedValues">Values planned by earlier documents for the same entity.</param>
    /// <returns>The validated, mutation-free PATCH plan.</returns>
    internal EfCorePatchPlan<TEntity> BuildPlan(
        EntityEntry<TEntity> entry,
        TEntity entity,
        JsonElement patchDocument,
        IReadOnlySet<string> keyPropertyNames,
        EfCorePatchUnknownFieldBehavior unknownFieldBehavior,
        IDictionary<string, object?> plannedValues)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(entity);
        ArgumentNullException.ThrowIfNull(keyPropertyNames);
        ArgumentNullException.ThrowIfNull(plannedValues);

        var operations = new List<EfCorePatchOperation>();
        foreach (var patchProperty in patchDocument.EnumerateObject())
        {
            if (!_jsonContract.TryGetPatchMember(patchProperty.Name, out var jsonMember)
                || jsonMember.ClrName is null
                || !_patchPropertiesByClrName.TryGetValue(
                    jsonMember.ClrName,
                    out var propertyContract))
            {
                ThrowIfStrictUnknownField(unknownFieldBehavior, patchProperty.Name, "unknown");
                continue;
            }

            var propertyInfo = propertyContract.PropertyInfo;
            if (keyPropertyNames.Contains(propertyInfo.Name))
            {
                throw new EfCorePatchValidationException(
                    $"PATCH cannot modify immutable resource key field '{patchProperty.Name}'.");
            }

            var currentValue = plannedValues.TryGetValue(propertyInfo.Name, out var plannedValue)
                ? plannedValue
                : propertyInfo.GetValue(entity);
            var value = JsonMergePatch.Apply(
                currentValue,
                propertyInfo.PropertyType,
                patchProperty.Value,
                propertyContract.JsonMember.ValueSerializerOptions);

            operations.Add(new EfCorePatchOperation(propertyInfo, value));
            plannedValues[propertyInfo.Name] = value;
        }

        return new EfCorePatchPlan<TEntity>(entry, operations);
    }

    /// <summary>
    /// Applies a validated PATCH plan while capturing the original tracked property state.
    /// </summary>
    /// <param name="patchPlan">The plan to apply.</param>
    /// <param name="snapshots">The collection that receives original property snapshots.</param>
    internal void ApplyPlan(
        EfCorePatchPlan<TEntity> patchPlan,
        ICollection<EfCorePatchPropertySnapshot<TEntity>> snapshots)
    {
        ArgumentNullException.ThrowIfNull(patchPlan);
        ArgumentNullException.ThrowIfNull(snapshots);

        foreach (var operation in patchPlan.Operations)
        {
            if (!snapshots.Any(snapshot =>
                    ReferenceEquals(snapshot.Entry.Entity, patchPlan.Entry.Entity)
                    && string.Equals(
                        snapshot.PropertyName,
                        operation.PropertyInfo.Name,
                        StringComparison.Ordinal)))
            {
                var propertyEntry = patchPlan.Entry.Property(operation.PropertyInfo.Name);
                snapshots.Add(new EfCorePatchPropertySnapshot<TEntity>(
                    patchPlan.Entry,
                    operation.PropertyInfo.Name,
                    propertyEntry.CurrentValue,
                    propertyEntry.IsModified));
            }

            patchPlan.Entry.Property(operation.PropertyInfo.Name).CurrentValue = operation.Value;
        }
    }

    /// <summary>
    /// Restores tracked property values and modification flags captured before plan application.
    /// </summary>
    /// <param name="snapshots">The snapshots to restore.</param>
    internal void RestoreChanges(IEnumerable<EfCorePatchPropertySnapshot<TEntity>> snapshots)
    {
        ArgumentNullException.ThrowIfNull(snapshots);

        foreach (var snapshot in snapshots.Reverse())
        {
            var propertyEntry = snapshot.Entry.Property(snapshot.PropertyName);
            propertyEntry.CurrentValue = snapshot.CurrentValue;
            propertyEntry.IsModified = snapshot.IsModified;
        }
    }

    private static IReadOnlyDictionary<string, PatchPropertyContract> BuildPatchPropertyMap(
        IModel model,
        JsonObjectContract jsonContract)
    {
        var entityType = model.FindEntityType(typeof(TEntity))
            ?? throw new InvalidOperationException(
                $"Entity type '{typeof(TEntity).Name}' is not part of the EF Core model.");
        var mappedProperties = new Dictionary<string, PropertyInfo>(StringComparer.Ordinal);
        foreach (var mappedProperty in entityType.GetProperties())
        {
            var property = mappedProperty.PropertyInfo;
            if (property is null || !property.CanWrite || property.GetIndexParameters().Length > 0)
            {
                continue;
            }

            mappedProperties[property.Name] = property;
        }

        var map = new Dictionary<string, PatchPropertyContract>(StringComparer.Ordinal);
        foreach (var jsonMember in jsonContract.Members)
        {
            if (!jsonMember.CanDeserialize
                || jsonMember.ClrName is null
                || !mappedProperties.TryGetValue(jsonMember.ClrName, out var property))
            {
                continue;
            }

            map[property.Name] = new PatchPropertyContract(property, jsonMember);
        }

        return map;
    }

    private static void ThrowIfStrictUnknownField(
        EfCorePatchUnknownFieldBehavior unknownFieldBehavior,
        string propertyName,
        string reason)
    {
        if (unknownFieldBehavior == EfCorePatchUnknownFieldBehavior.Strict)
        {
            throw new EfCorePatchValidationException(
                $"PATCH field '{propertyName}' is {reason} for this resource.");
        }
    }

    private sealed record PatchPropertyContract(
        PropertyInfo PropertyInfo,
        JsonMemberContract JsonMember);
}
