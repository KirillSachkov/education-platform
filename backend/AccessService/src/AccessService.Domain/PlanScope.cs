namespace AccessService.Domain;

/// <summary>Persisted catalog discriminator. Historical TRAINER rows remain isolated from platform reads.</summary>
public enum PlanScope
{
    /// <summary>Active platform catalog.</summary>
    PLATFORM,

    /// <summary>Retired trainer offers; retained for stored rows and billing protocol compatibility.</summary>
    TRAINER,
}