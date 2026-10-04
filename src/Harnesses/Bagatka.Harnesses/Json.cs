using System.Text.Json;

namespace Bagatka.Harnesses;

// Reading the loosely shaped JSON harnesses write: a missing property is null, never an exception.
internal static class Json
{
    public static JsonElement? Property(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        bool found = element.TryGetProperty(name, out JsonElement value);
        return found ? value : null;
    }
}
