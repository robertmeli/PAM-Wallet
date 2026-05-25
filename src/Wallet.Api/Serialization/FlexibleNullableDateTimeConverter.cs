using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Wallet.Api.Serialization;

public sealed class FlexibleNullableDateTimeConverter : JsonConverter<DateTime?>
{
    private static readonly string[] SupportedFormats =
    [
        "O",
        "yyyy-MM-dd",
        "yyyy-MM-ddTHH:mm:ss",
        "yyyy-MM-ddTHH:mm:ssZ",
        "yyyy-MM-ddTHH:mm:ss.fff",
        "yyyy-MM-ddTHH:mm:ss.fffZ"
    ];

    public override DateTime? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            return null;
        }

        if (reader.TokenType == JsonTokenType.Number && reader.TryGetInt64(out var epoch))
        {
            return epoch > 9_999_999_999
                ? DateTimeOffset.FromUnixTimeMilliseconds(epoch).UtcDateTime
                : DateTimeOffset.FromUnixTimeSeconds(epoch).UtcDateTime;
        }

        if (reader.TokenType != JsonTokenType.String)
        {
            throw new JsonException("expiresAt must be a string, null, or unix timestamp.");
        }

        var value = reader.GetString();
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (DateTime.TryParseExact(
                value,
                SupportedFormats,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var exact))
        {
            return NormalizeUtc(exact);
        }

        if (DateTime.TryParse(
                value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var parsed))
        {
            return NormalizeUtc(parsed);
        }

        throw new JsonException($"Invalid expiresAt format: '{value}'.");
    }

    public override void Write(Utf8JsonWriter writer, DateTime? value, JsonSerializerOptions options)
    {
        if (value is null)
        {
            writer.WriteNullValue();
            return;
        }

        writer.WriteStringValue(NormalizeUtc(value.Value).ToString("O", CultureInfo.InvariantCulture));
    }

    private static DateTime NormalizeUtc(DateTime value)
    {
        return value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
        };
    }
}
