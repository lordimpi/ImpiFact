using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace ImpiFact.Infrastructure.Persistence.Conversions;

/// <summary>
/// Columnas JSON (Modelo de datos detallado, sección 8, punto 6): un objeto se guarda serializado en una sola
/// columna, solo para datos que no se consultan por campo. Todas usan el mismo conversor y las mismas opciones
/// fijas de System.Text.Json, para que el mismo objeto dé siempre el mismo texto en los tres motores.
/// </summary>
public static class JsonColumn
{
    /// <summary>Anotación (marca en el modelo de EF) que deja ver a cada proveedor qué columnas son JSON.</summary>
    public const string AnnotationName = "ImpiFact:JsonColumn";

    /// <summary>
    /// Opciones fijas: nombres en snake_case como el contrato JSON heredado del PHP, sin sangría, enums como
    /// número y sin omitir valores nulos. Son de solo lectura para que nadie las cambie en tiempo de ejecución.
    /// </summary>
    public static JsonSerializerOptions SerializerOptions { get; } = CreateOptions();

    /// <summary>Guarda la propiedad como JSON y compara su contenido para detectar cambios dentro del objeto.</summary>
    public static PropertyBuilder<T> HasJsonConversion<T>(this PropertyBuilder<T> builder)
    {
        builder.HasConversion(new JsonValueConverter<T>(), new JsonValueComparer<T>());
        builder.HasAnnotation(AnnotationName, true);
        return builder;
    }

    /// <summary>Indica si la propiedad se configuró con <see cref="HasJsonConversion{T}"/>.</summary>
    public static bool IsJsonColumn(IReadOnlyProperty property) => property.FindAnnotation(AnnotationName) is not null;

    internal static string Serialize<T>(T? value) => JsonSerializer.Serialize(value, SerializerOptions);

    internal static T Deserialize<T>(string json) =>
        JsonSerializer.Deserialize<T>(json, SerializerOptions) ?? throw new InvalidOperationException("A JSON column contains null.");

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.General)
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            DictionaryKeyPolicy = JsonNamingPolicy.SnakeCaseLower,
            DefaultIgnoreCondition = JsonIgnoreCondition.Never,
            Encoder = JavaScriptEncoder.Default,
            WriteIndented = false,
            TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
        };
        options.MakeReadOnly();
        return options;
    }
}

/// <summary>Convierte el objeto a texto JSON al escribir y de vuelta al leer.</summary>
internal sealed class JsonValueConverter<T>() : ValueConverter<T, string>(
    value => JsonColumn.Serialize(value),
    json => JsonColumn.Deserialize<T>(json));

/// <summary>
/// Compara dos valores por su JSON. Sin esto EF compararía la referencia del objeto y no vería un cambio hecho
/// dentro de él; la copia de referencia (snapshot) también se hace pasando por JSON.
/// </summary>
internal sealed class JsonValueComparer<T>() : ValueComparer<T>(
    (left, right) => JsonColumn.Serialize(left) == JsonColumn.Serialize(right),
    value => JsonColumn.Serialize(value).GetHashCode(StringComparison.Ordinal),
    value => JsonColumn.Deserialize<T>(JsonColumn.Serialize(value)));
