using System.Text.Json;
using System.Text.Json.Serialization;

namespace ExtractLoz;

internal class LowerCaseEnumConverter<T> : JsonConverter<T> where T : struct, Enum
{
    public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var enumText = reader.GetString();

        if (Enum.TryParse(enumText, ignoreCase: true, out T value))
        {
            return value;
        }

        throw new JsonException($"Unable to convert \"{enumText}\" to enum \"{typeof(T)}\".");
    }

    public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options)
    {
        var enumString = value.ToString().ToLowerInvariant();
        writer.WriteStringValue(enumString);
    }
}

internal class LowerCaseEnumConverterFactory : JsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert)
    {
        return typeToConvert.IsEnum || (Nullable.GetUnderlyingType(typeToConvert)?.IsEnum ?? false);
    }

    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options)
    {
        var enumType = Nullable.GetUnderlyingType(typeToConvert) ?? typeToConvert;
        var converterType = typeof(LowerCaseEnumConverter<>).MakeGenericType(enumType);
        return (JsonConverter)Activator.CreateInstance(converterType);
    }
}

internal class LowerCaseNamingPolicy : JsonNamingPolicy
{
    public override string ConvertName(string name)
    {
        return name.ToLowerInvariant();
    }
}