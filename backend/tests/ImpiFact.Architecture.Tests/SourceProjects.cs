using System.Xml.Linq;

namespace ImpiFact.Architecture.Tests;

/// <summary>Un proyecto de <c>backend/src</c> leído desde su archivo .csproj.</summary>
public sealed record SourceProject(string Name, IReadOnlyList<string> ProjectReferences, IReadOnlyList<string> PackageReferences)
{
    public override string ToString() => Name;
}

public static class SourceProjects
{
    /// <summary>Todos los proyectos de <c>backend/src</c>, ordenados por nombre.</summary>
    public static IReadOnlyList<SourceProject> All()
    {
        var src = Path.Combine(FindBackendRoot(), "src");
        return Directory.EnumerateFiles(src, "*.csproj", SearchOption.AllDirectories)
            .Where(path => !IsBuildOutput(path))
            .Select(Read)
            .OrderBy(project => project.Name, StringComparer.Ordinal)
            .ToList();
    }

    private static SourceProject Read(string path)
    {
        var project = XDocument.Load(path);
        var projectReferences = project.Descendants("ProjectReference")
            .Select(reference => ProjectName((string)reference.Attribute("Include")!))
            .ToList();
        var packageReferences = project.Descendants("PackageReference")
            .Select(reference => (string)reference.Attribute("Include")!)
            .ToList();
        return new SourceProject(Path.GetFileNameWithoutExtension(path), projectReferences, packageReferences);
    }

    /// <summary>
    /// Nombre del proyecto a partir de la ruta de un ProjectReference. Los .csproj escriben las rutas con «\»,
    /// que en Linux no separa carpetas; por eso se cambia por «/» antes de quitar la carpeta y la extensión.
    /// </summary>
    internal static string ProjectName(string includePath) =>
        Path.GetFileNameWithoutExtension(includePath.Replace('\\', '/'));

    private static bool IsBuildOutput(string path) =>
        path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Any(part => part is "bin" or "obj");

    // Sube desde la carpeta de salida de las pruebas hasta encontrar la carpeta backend (la que tiene ImpiFact.slnx).
    private static string FindBackendRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "ImpiFact.slnx")))
            {
                return dir.FullName;
            }
        }

        throw new DirectoryNotFoundException("ImpiFact.slnx not found above " + AppContext.BaseDirectory);
    }
}
