using System.Text;
using ImpiFact.Dian.Signing;

namespace ImpiFact.Dian.Tests.Signing;

/// <summary>
/// Firma todos los documentos de referencia (tests/golden) y los escribe en disco junto con el certificado
/// público, para que tools/signature-check los verifique con xmlsec1, una herramienta de verificación de
/// firmas XML independiente de .NET. La prueba está marcada [Explicit]: NUnit solo la ejecuta cuando se
/// pide por nombre, y solo escribe en un directorio fuera del repositorio. La clave privada nunca se escribe.
/// </summary>
public class SignatureSampleWriter
{
    public const string OutputDirectoryVariable = "IMPIFACT_SIGNATURE_SAMPLE_DIR";

    [Test]
    [Explicit("Writes files for tools/signature-check; run it on demand.")]
    public void Writes_a_signed_sample_for_the_independent_check()
    {
        var output = Environment.GetEnvironmentVariable(OutputDirectoryVariable);
        if (string.IsNullOrWhiteSpace(output))
        {
            Assert.Fail($"Set {OutputDirectoryVariable} to a directory outside the repository.");
            return;
        }

        var directory = Path.GetFullPath(output);
        Assert.That(IsInside(directory, RepositoryRoot()), Is.False, "The sample must be written outside the repository.");

        var signingTime = new DateTimeOffset(2026, 11, 15, 15, 30, 45, 123, TimeSpan.Zero);
        var utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        var signedDirectory = Path.Combine(directory, "signed");
        Directory.CreateDirectory(signedDirectory);

        // Un archivo por caso de referencia (facturas, notas crédito, nota débito), llamado <type>.<case>.xml.
        foreach (var fixture in GoldenFixtures.All())
        {
            var signed = XmlSigner.Sign(fixture.ExpectedXml, TestCertificates.Signer, signingTime);
            File.WriteAllText(Path.Combine(signedDirectory, $"{fixture.DocumentType}.{fixture.Name}.xml"), signed, utf8);
        }

        File.WriteAllText(Path.Combine(directory, "signer.pem"), TestCertificates.Signer.ExportCertificatePem(), utf8);
        TestContext.Out.WriteLine("Signed samples written to " + directory);
    }

    private static string RepositoryRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            // Busca la raíz del repositorio git: tiene una carpeta .git, o un archivo .git si es un worktree
            // (una copia de trabajo adicional del mismo repositorio). Así ninguna muestra queda dentro del repositorio.
            if (Directory.Exists(Path.Combine(dir.FullName, ".git")) || File.Exists(Path.Combine(dir.FullName, ".git")))
            {
                return dir.FullName;
            }
        }

        throw new DirectoryNotFoundException("Repository root not found above " + AppContext.BaseDirectory);
    }

    private static bool IsInside(string path, string root)
    {
        var relative = Path.GetRelativePath(root, path);
        return relative == "." || !(relative.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relative));
    }
}
