using CommentService.Domain;
using Common;

namespace CommentService.IntegrationTests.Domain;

/// <summary>
///     Guards the invariant that every supported comment target type fits within the
///     <see cref="EntityReference.MAX_TYPE_LENGTH" /> column constraint (see migration
///     <c>20260405114919</c>). If a new enum value is added and marked supported but its
///     name exceeds the column width, Postgres would silently truncate it at insert time.
/// </summary>
public sealed class CommentEntityReferenceTests
{
    [Fact]
    public void AllSupportedEntityTypes_FitWithinColumnConstraint()
    {
        var offenders = new List<string>();

        foreach (EntityType value in Enum.GetValues<EntityType>())
        {
            if (!CommentEntityReference.IsSupported(value))
            {
                continue;
            }

            string persisted = value.ToString().ToLowerInvariant();
            if (persisted.Length > EntityReference.MAX_TYPE_LENGTH)
            {
                offenders.Add($"{value} ({persisted.Length} chars)");
            }
        }

        Assert.Empty(offenders);
    }
}
