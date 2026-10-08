using System.Text.Json.Serialization;

namespace Common;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum EntityType
{
    Course,
    Module,
    Project,
    Issue,
    Quiz,
    Material,
    Collection,
}
