using Microsoft.Extensions.Options;

namespace ProgressService.Core.Configuration;

public sealed class GamificationOptionsValidator : IValidateOptions<GamificationOptions>
{
    public ValidateOptionsResult Validate(string? name, GamificationOptions options)
    {
        List<string> failures = [];

        if (options.Awards.IssueApproved <= 0)
        {
            failures.Add("Gamification:Awards:IssueApproved must be greater than zero.");
        }

        if (options.Awards.ModuleCompleted <= 0)
        {
            failures.Add("Gamification:Awards:ModuleCompleted must be greater than zero.");
        }

        if (options.Awards.ProjectCompleted <= 0)
        {
            failures.Add("Gamification:Awards:ProjectCompleted must be greater than zero.");
        }

        if (options.Awards.MaterialViewed <= 0)
        {
            failures.Add("Gamification:Awards:MaterialViewed must be greater than zero.");
        }

        if (options.Levels.Count == 0)
        {
            failures.Add("Gamification:Levels must contain at least one level.");
            return ValidateOptionsResult.Fail(failures);
        }

        List<GamificationLevelOptions> orderedLevels = [.. options.Levels.OrderBy(x => x.Level)];

        if (orderedLevels[0].Level != 1)
        {
            failures.Add("Gamification:Levels must start from level 1.");
        }

        if (orderedLevels[0].RequiredXp != 0)
        {
            failures.Add("Gamification:Levels first level must have RequiredXp = 0.");
        }

        for (int index = 0; index < orderedLevels.Count; index++)
        {
            GamificationLevelOptions level = orderedLevels[index];

            if (level.Level <= 0)
            {
                failures.Add("Gamification:Levels level number must be greater than zero.");
            }

            if (level.RequiredXp < 0)
            {
                failures.Add($"Gamification:Levels[{index}].RequiredXp must be greater than or equal to zero.");
            }

            if (index == 0)
            {
                continue;
            }

            GamificationLevelOptions previous = orderedLevels[index - 1];

            if (level.Level != previous.Level + 1)
            {
                failures.Add("Gamification:Levels must be sequential without gaps.");
            }

            if (level.RequiredXp <= previous.RequiredXp)
            {
                failures.Add("Gamification:Levels RequiredXp must be strictly increasing.");
            }
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
