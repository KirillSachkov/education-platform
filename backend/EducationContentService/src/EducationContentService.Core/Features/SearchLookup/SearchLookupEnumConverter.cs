using EducationContentService.Contracts.SearchLookup;

namespace EducationContentService.Core.Features.SearchLookup;

internal static class SearchLookupEnumConverter
{
    public static PublicationStatus ToPublicationStatus(string value)
    {
        if (Enum.TryParse<PublicationStatus>(value, ignoreCase: true, out PublicationStatus status))
        {
            return status;
        }

        // boundary: DB→enum projection in Dapper rows; an unknown value indicates corrupted stored data.
        throw new InvalidOperationException($"Unknown publication status '{value}'.");
    }
}
