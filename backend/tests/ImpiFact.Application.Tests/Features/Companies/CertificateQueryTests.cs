using ImpiFact.Application.Abstractions.Companies;
using ImpiFact.Application.Features.Companies;
using ImpiFact.Application.Features.Companies.ListCertificates;
using ImpiFact.Domain.Companies;

namespace ImpiFact.Application.Tests.Features.Companies;

public sealed class CertificateQueryTests
{
    // 16-nov-2026 a las 03:00 UTC son las 22:00 del 15-nov en Colombia: el día que cuenta es el 15.
    private readonly FakeClock _clock = new(new DateTimeOffset(2026, 11, 16, 3, 0, 0, TimeSpan.Zero));
    private FakeCompanyRepository _companies = null!;
    private FakeCompanyReads _reads = null!;
    private Company _own = null!;

    [SetUp]
    public void SetUp()
    {
        _companies = new FakeCompanyRepository();
        _reads = new FakeCompanyReads();
        _own = CompanySamples.Company();
        _companies.Stored.Add(_own);
    }

    [Test]
    public async Task Listing_returns_the_metadata_with_days_left_counted_in_colombia()
    {
        var notAfter = new DateTimeOffset(2026, 11, 25, 4, 0, 0, TimeSpan.Zero); // 24-nov 23:00 en Colombia.
        _reads.Certificates.Add((_own.Id, new CertificateMetadata(
            Guid.CreateVersion7(), "9001234568.p12", "AB", "CN=Empresa", "CN=CA", notAfter.AddYears(-1), notAfter, true, notAfter.AddYears(-1))));

        var result = await new ListCertificatesHandler(_companies, _reads, CompanySamples.CompanyKey(_own.Id), _clock)
            .HandleAsync(new ListCertificatesQuery(_own.IdentificationNumber), CancellationToken.None);

        var certificate = result.Value.Certificates.Single();
        Assert.Multiple(() =>
        {
            Assert.That(certificate.Name, Is.EqualTo("9001234568.p12"));
            Assert.That(certificate.IsActive, Is.True);
            Assert.That(certificate.DaysLeft, Is.EqualTo(9));
        });
    }

    [Test]
    public async Task Listing_with_another_companys_nit_is_forbidden()
    {
        var result = await new ListCertificatesHandler(_companies, _reads, CompanySamples.CompanyKey(_own.Id), _clock)
            .HandleAsync(new ListCertificatesQuery("800111222"), CancellationToken.None);

        Assert.That(result.Error, Is.EqualTo(CompanyErrors.ForeignNit));
    }

    [Test]
    public async Task End_date_uses_the_php_format_and_the_colombian_date()
    {
        _companies.Stored.Add(new Certificate
        {
            CompanyId = _own.Id, Name = "c.p12", SecretRef = "ref", Thumbprint = "AB", Subject = "CN=E", Issuer = "CN=CA",
            NotAfter = new DateTimeOffset(2027, 1, 1, 2, 0, 0, TimeSpan.Zero), IsActive = true,
        });

        var result = await new GetCertificateEndDateHandler(_companies, CompanySamples.CompanyKey(_own.Id), _clock)
            .HandleAsync(new GetCertificateEndDateQuery(null), CancellationToken.None);

        Assert.That(result.Value.ExpirationDate, Is.EqualTo("31/12/2026"));
        Assert.That(result.Value.DaysLeft, Is.EqualTo(46));
    }

    [Test]
    public async Task End_date_without_an_active_certificate_is_not_found()
    {
        var result = await new GetCertificateEndDateHandler(_companies, CompanySamples.CompanyKey(_own.Id), _clock)
            .HandleAsync(new GetCertificateEndDateQuery(null), CancellationToken.None);

        Assert.That(result.Error, Is.EqualTo(CompanyErrors.CertificateNotFound));
    }
}
