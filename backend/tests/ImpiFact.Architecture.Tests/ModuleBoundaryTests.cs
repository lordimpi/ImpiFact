using System.Reflection;
using NetArchTest.Rules;

namespace ImpiFact.Architecture.Tests;

/// <summary>
/// Regla 4 de Arquitectura detallada, sección 1: ningún módulo lee las tablas de otro. Los casos de uso de un módulo
/// (<c>Application/Features/&lt;Módulo&gt;</c>) no usan los casos de uso ni las entidades de otro módulo; cuando
/// necesitan algo de otro módulo, lo piden a un puerto de <c>Application/Abstractions</c> (por ejemplo, el almacén de
/// API keys de Identity). Los catálogos DIAN y las piezas comunes los usan todos.
/// </summary>
public class ModuleBoundaryTests
{
    private static readonly string[] DomainModules = ["Identity", "Companies", "Masters", "Documents", "Billing", "Platform"];

    /// <summary>
    /// Excepciones aceptadas y su motivo. Company crea el cliente por defecto de cada empresa nueva, como el PHP
    /// (Masters todavía no tiene casos de uso; cuando los tenga, se pasa a un puerto).
    /// </summary>
    private static readonly Dictionary<string, string[]> AllowedDomainModules = new(StringComparer.Ordinal)
    {
        ["Companies"] = ["Masters"],
    };

    public static IEnumerable<TestCaseData> FeatureModules() =>
        Assembly.Load(new AssemblyName("ImpiFact.Application")).GetTypes()
            .Select(type => type.Namespace)
            .OfType<string>()
            .Where(name => name.StartsWith("ImpiFact.Application.Features.", StringComparison.Ordinal))
            .Select(name => name.Split('.')[3])
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .Select(module => new TestCaseData(module).SetArgDisplayNames(module));

    [TestCaseSource(nameof(FeatureModules))]
    public void A_feature_module_uses_no_other_feature_module(string module)
    {
        var others = FeatureModules().Select(data => (string)data.Arguments[0]!).Where(other => other != module)
            .Select(other => $"ImpiFact.Application.Features.{other}")
            .ToArray();

        var result = FeatureTypes(module).ShouldNot().HaveDependencyOnAny(others).GetResult();

        Assert.That(result.FailingTypeNames ?? [], Is.Empty);
    }

    [TestCaseSource(nameof(FeatureModules))]
    public void A_feature_module_uses_only_its_own_domain_entities(string module)
    {
        var allowed = AllowedDomainModules.GetValueOrDefault(module, []);
        var forbidden = DomainModules.Where(other => other != module && !allowed.Contains(other))
            .Select(other => $"ImpiFact.Domain.{other}")
            .ToArray();

        var result = FeatureTypes(module).ShouldNot().HaveDependencyOnAny(forbidden).GetResult();

        Assert.That(result.FailingTypeNames ?? [], Is.Empty, "Ask for it through a port in Application/Abstractions instead.");
    }

    private static PredicateList FeatureTypes(string module) =>
        Types.InAssembly(Assembly.Load(new AssemblyName("ImpiFact.Application")))
            .That()
            .ResideInNamespaceStartingWith($"ImpiFact.Application.Features.{module}");
}
