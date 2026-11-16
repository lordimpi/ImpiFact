using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ImpiFact.Infrastructure.Persistence.Conventions;

/// <summary>Reglas para los nombres físicos de tablas, columnas, llaves e índices.</summary>
internal static class DbIdentifier
{
    /// <summary>Largo máximo de un nombre en PostgreSQL; MySQL admite 64, así que 63 sirve para ambos.</summary>
    public const int MaxLength = 63;

    private const int HashLength = 8;

    /// <summary>
    /// Pasa un nombre de C# a snake_case (minúsculas separadas por guion bajo): <c>DisplayName</c> queda
    /// <c>display_name</c>. Usa la misma regla que System.Text.Json, que ya resuelve siglas y números.
    /// </summary>
    public static string ToSnakeCase(string name) => JsonNamingPolicy.SnakeCaseLower.ConvertName(name);

    /// <summary>
    /// Deja el nombre en 63 caracteres como máximo. PostgreSQL corta en silencio los nombres largos y dos de ellos
    /// podrían quedar iguales; aquí se corta el nombre y se termina con «_» y una huella de 8 caracteres
    /// hexadecimales del nombre completo (SHA-256, una función que da siempre el mismo resumen para el mismo
    /// texto), así el resultado es estable y distinto para nombres distintos.
    /// </summary>
    public static string Shorten(string name)
    {
        if (name.Length <= MaxLength)
        {
            return name;
        }

        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(name)))[..HashLength];
        return name[..(MaxLength - HashLength - 1)] + "_" + hash;
    }
}
