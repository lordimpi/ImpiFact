namespace ImpiFact.Architecture.Tests;

public class SourceProjectsTests
{
    // Las rutas de los .csproj usan «\» aunque las pruebas corran en Linux (como en la integración continua).
    [TestCase(@"..\ImpiFact.Domain\ImpiFact.Domain.csproj")]
    [TestCase("../ImpiFact.Domain/ImpiFact.Domain.csproj")]
    public void Project_name_is_read_from_a_reference_with_either_separator(string includePath)
    {
        Assert.That(SourceProjects.ProjectName(includePath), Is.EqualTo("ImpiFact.Domain"));
    }
}
