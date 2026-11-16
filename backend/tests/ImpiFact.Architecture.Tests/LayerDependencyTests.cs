using System.Reflection;
using NetArchTest.Rules;

namespace ImpiFact.Architecture.Tests;

/// <summary>
/// Reglas de dependencia entre capas (Arquitectura detallada, sección 1). Revisan dos cosas: las referencias
/// declaradas en cada .csproj, que atrapan una dependencia aunque todavía no se use, y los tipos compilados,
/// que atrapan el uso real de una librería prohibida.
/// </summary>
public class LayerDependencyTests
{
    /// <summary>
    /// A qué proyectos puede referenciar cada proyecto de src. Es un máximo: un proyecto puede usar menos.
    /// ImpiFact.Dian no referencia a Domain (ADR-26: el núcleo DIAN recibe su propio modelo de entrada).
    /// La API, el Worker y el Migrator referencian a los proveedores de base de datos porque son quienes eligen cuál usar.
    /// </summary>
    private static readonly Dictionary<string, string[]> AllowedReferences = new(StringComparer.Ordinal)
    {
        ["ImpiFact.Domain"] = [],
        ["ImpiFact.Dian"] = [],
        ["ImpiFact.Application"] = ["ImpiFact.Domain", "ImpiFact.Dian"],
        ["ImpiFact.Infrastructure"] = ["ImpiFact.Application", "ImpiFact.Dian", "ImpiFact.Domain"],
        ["ImpiFact.Infrastructure.Postgres"] = ["ImpiFact.Infrastructure"],
        ["ImpiFact.Api"] = ["ImpiFact.Application", "ImpiFact.Infrastructure", "ImpiFact.Infrastructure.Postgres"],
        ["ImpiFact.Worker"] = ["ImpiFact.Application", "ImpiFact.Infrastructure", "ImpiFact.Infrastructure.Postgres"],
        ["ImpiFact.Migrator"] = ["ImpiFact.Application", "ImpiFact.Infrastructure", "ImpiFact.Infrastructure.Postgres"],
    };

    /// <summary>Proyectos de composición: arman los servicios y arrancan un proceso. Ningún otro proyecto los referencia.</summary>
    private static readonly string[] CompositionRoots = ["ImpiFact.Api", "ImpiFact.Worker", "ImpiFact.Migrator"];

    /// <summary>
    /// Paquetes que Application puede usar (Arquitectura, sección 2): validación, mapeo y registro de
    /// decoradores. Además puede usar las abstracciones de Microsoft.Extensions (solo interfaces).
    /// </summary>
    private static readonly string[] ApplicationPackagePrefixes = ["FluentValidation", "Mapster", "Scrutor"];

    /// <summary>Librerías de acceso a datos, web o nube que Application nunca debe usar directamente.</summary>
    private static readonly string[] InfrastructureNamespaces =
    [
        "Microsoft.EntityFrameworkCore", "Dapper", "Npgsql", "Microsoft.AspNetCore", "Azure", "Amazon", "Google.Cloud",
    ];

    public static IEnumerable<TestCaseData> Projects() =>
        SourceProjects.All().Select(project => new TestCaseData(project).SetArgDisplayNames(project.Name));

    [Test]
    public void Every_source_project_is_declared_in_the_layer_graph()
    {
        var undeclared = SourceProjects.All().Select(project => project.Name).Where(name => !AllowedReferences.ContainsKey(name));

        Assert.That(undeclared, Is.Empty, "Add new projects to AllowedReferences with the references their layer allows.");
    }

    [TestCaseSource(nameof(Projects))]
    public void Project_references_follow_the_layer_graph(SourceProject project)
    {
        Assume.That(AllowedReferences.ContainsKey(project.Name), "Undeclared projects are reported by the graph test.");

        var forbidden = project.ProjectReferences.Except(AllowedReferences[project.Name], StringComparer.Ordinal);

        Assert.That(forbidden, Is.Empty, $"{project.Name} references projects its layer does not allow.");
    }

    [TestCaseSource(nameof(CompositionRoots))]
    public void No_source_project_references_a_composition_root(string root)
    {
        var dependents = SourceProjects.All().Where(project => project.ProjectReferences.Contains(root, StringComparer.Ordinal))
            .Select(project => project.Name);

        Assert.That(dependents, Is.Empty, $"{root} only composes; move what others need into Application or Infrastructure.");
    }

    [Test]
    public void Domain_references_no_packages()
    {
        var domain = SourceProjects.All().Single(project => project.Name == "ImpiFact.Domain");

        Assert.That(domain.PackageReferences, Is.Empty);
    }

    [Test]
    public void Application_only_uses_validation_mapping_and_abstraction_packages()
    {
        var application = SourceProjects.All().Single(project => project.Name == "ImpiFact.Application");

        var forbidden = application.PackageReferences.Where(package => !IsAllowedApplicationPackage(package));

        Assert.That(forbidden, Is.Empty);
    }

    [Test]
    public void Domain_types_depend_on_no_other_layer()
    {
        var result = Types.InAssembly(Load("ImpiFact.Domain"))
            .ShouldNot()
            .HaveDependencyOnAny("ImpiFact.Application", "ImpiFact.Infrastructure", "ImpiFact.Dian", "ImpiFact.Api")
            .GetResult();

        Assert.That(result.FailingTypeNames ?? [], Is.Empty);
    }

    [Test]
    public void Application_types_do_not_use_data_web_or_cloud_libraries()
    {
        var result = Types.InAssembly(Load("ImpiFact.Application"))
            .ShouldNot()
            .HaveDependencyOnAny([.. InfrastructureNamespaces, "ImpiFact.Infrastructure", "ImpiFact.Api"])
            .GetResult();

        Assert.That(result.FailingTypeNames ?? [], Is.Empty);
    }

    [Test]
    public void Dian_types_depend_on_no_other_layer()
    {
        var result = Types.InAssembly(Load("ImpiFact.Dian"))
            .ShouldNot()
            .HaveDependencyOnAny("ImpiFact.Domain", "ImpiFact.Application", "ImpiFact.Infrastructure", "ImpiFact.Api")
            .GetResult();

        Assert.That(result.FailingTypeNames ?? [], Is.Empty);
    }

    private static bool IsAllowedApplicationPackage(string package) =>
        ApplicationPackagePrefixes.Any(prefix => package.StartsWith(prefix, StringComparison.Ordinal))
        || (package.StartsWith("Microsoft.Extensions.", StringComparison.Ordinal)
            && package.EndsWith(".Abstractions", StringComparison.Ordinal));

    // Carga por nombre: un proyecto todavía sin tipos no tiene una clase que sirva de ancla con typeof.
    private static Assembly Load(string name) => Assembly.Load(new AssemblyName(name));
}
