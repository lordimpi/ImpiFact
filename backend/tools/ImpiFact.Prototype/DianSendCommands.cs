// Fase 1b: envía una factura firmada por .NET al ambiente de habilitación de la DIAN (el ambiente de
// pruebas donde la DIAN valida el software antes de pasar a producción) y consulta el resultado.
//
// Uso (desde la carpeta backend/):
//   dotnet run --project tools/ImpiFact.Prototype -- send-test-set --cert <file.p12> --nit <nit> --dv <dígito>
//       --name "<razón social o nombre>" --software-id <id> --test-set-id <id> --number <990000000..995000000>
//       [--organization 1|2] [--regime 48|49] [--liability <código>] [--out <dir>] [--url <url>]
//   dotnet run --project tools/ImpiFact.Prototype -- status-zip --cert <file.p12> --zip-key <clave> [--out <dir>]
//
// Los secretos no van como argumentos (quedarían en el historial de la terminal): la contraseña del .p12 se
// lee de la variable de entorno IMPIFACT_P12_PASSWORD y el PIN del software de IMPIFACT_SOFTWARE_PIN; si no
// están, se piden por consola sin mostrarlos.
//
// send-test-set usa SendTestSetAsync (envío asíncrono del set de pruebas): la DIAN devuelve una clave del
// envío (ZipKey) y el resultado se consulta con GetStatusZip hasta que llega. La solicitud, la respuesta y
// el ApplicationResponse (el XML de respuesta firmado por la DIAN) se guardan en la carpeta de salida, que
// nunca puede estar dentro del repositorio.

using System.Globalization;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using ImpiFact.Dian.Cufe;
using ImpiFact.Dian.Signing;
using ImpiFact.Dian.Soap;
using ImpiFact.Dian.Ubl;

internal static class DianSendCommands
{
    private const string HabilitationUrl = "https://vpfe-hab.dian.gov.co/WcfDianCustomerServices.svc";
    private const string HabilitationQrUrl = "https://catalogo-vpfe-hab.dian.gov.co/document/searchqr?documentkey=";

    // Resolución de numeración de pruebas que la DIAN asigna a todo software en habilitación.
    private const string TestPrefix = "SETP";
    private const long TestRangeFrom = 990_000_000;
    private const long TestRangeTo = 995_000_000;
    private const string TestTechnicalKey = "fc8eac422eba16e22ffd8c6f94b3f40a6e38162c";

    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan PollTimeout = TimeSpan.FromMinutes(3);

    public static async Task<int> SendTestSetAsync(Dictionary<string, string> options)
    {
        var missing = new[] { "--cert", "--nit", "--dv", "--name", "--software-id", "--test-set-id", "--number" }
            .Where(name => !options.ContainsKey(name)).ToList();
        if (missing.Count > 0)
        {
            Console.Error.WriteLine("Missing arguments: " + string.Join(", ", missing));
            return 2;
        }

        var nit = options["--nit"];
        var checkDigit = options["--dv"];
        var expectedCheckDigit = NitCheckDigit(nit);
        if (checkDigit != expectedCheckDigit)
        {
            Console.Error.WriteLine($"The check digit of NIT {nit} is {expectedCheckDigit}, not {checkDigit}.");
            return 2;
        }

        if (!long.TryParse(options["--number"], NumberStyles.None, CultureInfo.InvariantCulture, out var number)
            || number < TestRangeFrom || number > TestRangeTo)
        {
            Console.Error.WriteLine($"--number must be between {TestRangeFrom} and {TestRangeTo}.");
            return 2;
        }

        if (OutputDirectory(options) is not { } outputDirectory)
        {
            return 2;
        }

        using var certificate = LoadCertificate(options["--cert"]);
        var pin = SecretFromEnvironmentOrConsole("IMPIFACT_SOFTWARE_PIN", "Software PIN: ");
        var url = options.GetValueOrDefault("--url") ?? HabilitationUrl;

        var now = DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(-5));
        var invoice = TestInvoice(options, nit, checkDigit, number, new SoftwareCredentials(options["--software-id"], pin), now);
        var built = InvoiceUblBuilder.Build(invoice);
        var signed = XmlSigner.Sign(built.Xml, certificate, now);

        // El consecutivo del nombre de archivo solo tiene que ser único por empresa, año y tipo; aquí se toma
        // del número de la factura para no llevar un contador aparte en el prototipo.
        var fileCounter = number % 100_000_000;
        var xmlEntry = DianFileNames.XmlEntry(DianFileKind.Invoice, nit, now.Year, fileCounter);
        var content = DianZip.CreateBase64(xmlEntry, signed, now);
        var request = new SendTestSetAsyncRequest(DianFileNames.SoapFileName(TestPrefix, number), content, options["--test-set-id"]);
        var envelope = DianSoapEnvelope.Build(request, url, certificate, now);

        var utf8 = new UTF8Encoding(false);
        File.WriteAllText(Path.Combine(outputDirectory, xmlEntry), signed, utf8);
        File.WriteAllText(Path.Combine(outputDirectory, "SendTestSetAsync.request.xml"), envelope, utf8);

        Console.WriteLine($"Invoice      {invoice.DocumentNumber}");
        Console.WriteLine($"CUFE         {built.Cufe}");
        Console.WriteLine($"Certificate  {certificate.Subject} (valid until {certificate.NotAfter:yyyy-MM-dd})");
        if (!certificate.Subject.Contains(nit, StringComparison.Ordinal))
        {
            Console.WriteLine($"Warning      the certificate subject does not contain NIT {nit}; the DIAN may reject the signature.");
        }

        Console.WriteLine($"Sending      {request.OperationName} to {url}");

        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(180) };
        var sendBody = await PostAsync(http, url, envelope);
        File.WriteAllText(Path.Combine(outputDirectory, "SendTestSetAsync.response.xml"), sendBody, utf8);
        var sendResponse = DianResponseReader.Read(sendBody);
        var zipKey = Report(sendResponse, outputDirectory, "SendTestSetAsync");
        if (zipKey is null)
        {
            Console.WriteLine("No ZipKey in the response; nothing to poll.");
            return 1;
        }

        return await PollStatusZipAsync(http, url, certificate, zipKey, outputDirectory);
    }

    public static async Task<int> StatusZipAsync(Dictionary<string, string> options)
    {
        if (!options.TryGetValue("--cert", out var certPath) || !options.TryGetValue("--zip-key", out var zipKey))
        {
            Console.Error.WriteLine("Missing arguments: --cert and --zip-key are required.");
            return 2;
        }

        if (OutputDirectory(options) is not { } outputDirectory)
        {
            return 2;
        }

        using var certificate = LoadCertificate(certPath);
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(180) };
        return await PollStatusZipAsync(http, options.GetValueOrDefault("--url") ?? HabilitationUrl, certificate, zipKey, outputDirectory);
    }

    // Consulta GetStatusZip cada pocos segundos hasta que la DIAN devuelve un resultado con IsValid, o hasta
    // agotar el plazo. Mientras procesa, la DIAN responde sin IsValid o con el código 98 («en proceso»).
    private static async Task<int> PollStatusZipAsync(
        HttpClient http, string url, X509Certificate2 certificate, string zipKey, string outputDirectory)
    {
        var deadline = DateTimeOffset.UtcNow + PollTimeout;
        for (var attempt = 1; ; attempt++)
        {
            await Task.Delay(PollInterval);
            var envelope = DianSoapEnvelope.Build(new GetStatusZipRequest(zipKey), url, certificate, DateTimeOffset.UtcNow);
            var body = await PostAsync(http, url, envelope);
            File.WriteAllText(Path.Combine(outputDirectory, $"GetStatusZip.{attempt}.response.xml"), body, new UTF8Encoding(false));
            var response = DianResponseReader.Read(body);
            if (response is DianResultResponse { Results: [{ IsValid: { } isValid, StatusCode: var code } result, ..] }
                && code != "98")
            {
                Report(response, outputDirectory, "GetStatusZip");
                return isValid ? 0 : 1;
            }

            if (response is not DianResultResponse)
            {
                Report(response, outputDirectory, "GetStatusZip");
                return 1;
            }

            if (DateTimeOffset.UtcNow > deadline)
            {
                Console.WriteLine($"Still processing after {PollTimeout.TotalMinutes} min. Retry later with: status-zip --zip-key {zipKey}");
                return 1;
            }

            Console.WriteLine($"Waiting      DIAN still processing (attempt {attempt})");
        }
    }

    // Imprime el resultado y devuelve la ZipKey cuando la respuesta trae una.
    private static string? Report(DianSoapResponse response, string outputDirectory, string operation)
    {
        switch (response)
        {
            case DianResultResponse result:
                string? zipKey = null;
                foreach (var item in result.Results)
                {
                    Console.WriteLine($"{operation,-12} IsValid={item.IsValid?.ToString() ?? "-"} StatusCode={item.StatusCode ?? "-"} {item.StatusDescription}");
                    if (!string.IsNullOrEmpty(item.StatusMessage))
                    {
                        Console.WriteLine($"             {item.StatusMessage}");
                    }

                    foreach (var error in item.ErrorMessages)
                    {
                        Console.WriteLine($"  - {error}");
                    }

                    if (item.XmlBase64Bytes is { Length: > 0 } applicationResponse)
                    {
                        var path = Path.Combine(outputDirectory, $"ApplicationResponse.{item.XmlFileName ?? "dian"}.xml");
                        File.WriteAllBytes(path, applicationResponse);
                        Console.WriteLine($"             ApplicationResponse saved to {path}");
                    }

                    zipKey ??= item.ZipKey;
                }

                if (zipKey is not null)
                {
                    Console.WriteLine($"ZipKey       {zipKey}");
                }

                return zipKey;
            case DianSoapFaultResponse fault:
                Console.WriteLine($"{operation,-12} SOAP Fault {fault.Code} {fault.Subcode}: {fault.Reason}");
                return null;
            case DianServiceUnavailableResponse:
                Console.WriteLine($"{operation,-12} DIAN service unavailable (non-XML body; see the saved response)");
                return null;
            default:
                Console.WriteLine($"{operation,-12} Unrecognized response (see the saved response)");
                return null;
        }
    }

    private static async Task<string> PostAsync(HttpClient http, string url, string envelope)
    {
        using var content = new StringContent(envelope, new UTF8Encoding(false), DianSoapEnvelope.ContentType);
        using var response = await http.PostAsync(url, content);
        var body = await response.Content.ReadAsStringAsync();
        Console.WriteLine($"HTTP         {(int)response.StatusCode} {response.ReasonPhrase}");
        return body;
    }

    private static X509Certificate2 LoadCertificate(string path)
    {
        var password = SecretFromEnvironmentOrConsole("IMPIFACT_P12_PASSWORD", "Certificate password: ");
        return X509CertificateLoader.LoadPkcs12FromFile(path, password);
    }

    private static string? OutputDirectory(Dictionary<string, string> options)
    {
        var directory = Path.GetFullPath(options.GetValueOrDefault("--out")
            ?? Path.Combine(Path.GetTempPath(), "impifact-dian-" + DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)));
        for (var dir = new DirectoryInfo(directory); dir is not null; dir = dir.Parent)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, ".git")) || File.Exists(Path.Combine(dir.FullName, ".git")))
            {
                Console.Error.WriteLine($"Refusing to write inside a repository: {directory}");
                return null;
            }
        }

        Directory.CreateDirectory(directory);
        Console.WriteLine($"Output       {directory}");
        return directory;
    }

    private static string SecretFromEnvironmentOrConsole(string variable, string prompt)
    {
        var value = Environment.GetEnvironmentVariable(variable);
        if (!string.IsNullOrEmpty(value))
        {
            return value;
        }

        Console.Write(prompt);
        var builder = new StringBuilder();
        while (true)
        {
            var key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Enter)
            {
                Console.WriteLine();
                return builder.ToString();
            }

            if (key.Key == ConsoleKey.Backspace)
            {
                if (builder.Length > 0)
                {
                    builder.Length--;
                }
            }
            else
            {
                builder.Append(key.KeyChar);
            }
        }
    }

    // Dígito de verificación del NIT (número de identificación tributaria) con el algoritmo de la DIAN:
    // pesos primos por posición desde la derecha, suma módulo 11.
    internal static string NitCheckDigit(string nit)
    {
        int[] weights = [3, 7, 13, 17, 19, 23, 29, 37, 41, 43, 47, 53, 59, 67, 71];
        var sum = 0;
        for (var i = 0; i < nit.Length; i++)
        {
            sum += (nit[nit.Length - 1 - i] - '0') * weights[i];
        }

        var remainder = sum % 11;
        return (remainder > 1 ? 11 - remainder : remainder).ToString(CultureInfo.InvariantCulture);
    }

    // Factura de prueba: el emisor es el contribuyente registrado en habilitación y el cliente es el
    // consumidor final (222222222222), con la misma forma que el caso golden «final-consumer» de ApiDian.
    private static SalesInvoice TestInvoice(
        Dictionary<string, string> options, string nit, string checkDigit, long number, SoftwareCredentials software, DateTimeOffset now)
    {
        var address = new PartyAddress(
            MunicipalityCode: options.GetValueOrDefault("--municipality") ?? "11001",
            CityName: options.GetValueOrDefault("--city") ?? "Bogotá, D.c. ",
            DepartmentName: options.GetValueOrDefault("--department") ?? "Bogotá",
            DepartmentCode: options.GetValueOrDefault("--department-code") ?? "11",
            AddressLine: options.GetValueOrDefault("--address") ?? "CALLE 1 # 1-01",
            CountryCode: "CO", CountryName: "Colombia", LanguageCode: "es");
        var iva19 = new TaxSubtotal { SchemeCode = "01", SchemeName = "IVA", TaxableAmount = 100_000m, TaxAmount = 19_000m, Percent = 19m };
        var today = DateOnly.FromDateTime(now.DateTime);

        return new SalesInvoice
        {
            Environment = DianEnvironment.Testing,
            QrCodeBaseUrl = HabilitationQrUrl,
            OperationTypeCode = "10",
            InvoiceTypeCode = "01",
            Resolution = new NumberingResolution(
                "18760000001", new DateOnly(2019, 1, 19), new DateOnly(2030, 1, 19), TestPrefix, TestRangeFrom, TestRangeTo, TestTechnicalKey),
            Number = number,
            IssueDate = today,
            IssueTime = TimeOnly.FromDateTime(now.DateTime),
            Software = software,
            LocalCurrencyCode = "COP",
            Supplier = new InvoiceParty
            {
                OrganizationTypeCode = options.GetValueOrDefault("--organization") ?? "2",
                IdentificationNumber = nit,
                CheckDigit = checkDigit,
                IdentificationTypeCode = "31",
                Name = options["--name"],
                Address = address,
                RegimeCode = options.GetValueOrDefault("--regime") ?? "48",
                LiabilityCode = options.GetValueOrDefault("--liability") ?? "R-99-PN",
                TaxSchemeCode = "01",
                TaxSchemeName = "IVA",
                Telephone = options.GetValueOrDefault("--phone") ?? "3000000000",
                Email = options.GetValueOrDefault("--email") ?? "facturacion@example.com",
            },
            Customer = new InvoiceParty
            {
                OrganizationTypeCode = "2", IdentificationNumber = "222222222222", CheckDigit = null, IdentificationTypeCode = "13",
                Name = "CONSUMIDOR FINAL", Address = null, RegimeCode = "49",
                LiabilityCode = "R-99-PN", TaxSchemeCode = "01", TaxSchemeName = "IVA",
            },
            Payment = new PaymentTerms("1", "10", today, 0),
            TaxSubtotals = [iva19],
            Totals = new MonetaryTotals
            {
                LineExtensionAmount = 100_000m, TaxExclusiveAmount = 100_000m, TaxInclusiveAmount = 119_000m, PayableAmount = 119_000m,
            },
            Lines =
            [
                new InvoiceLine
                {
                    UnitCode = "94", Quantity = 1m, LineExtensionAmount = 100_000m, IsFreeOfCharge = false,
                    Description = "Servicio de prueba de habilitación", ItemCode = "HAB01",
                    ItemScheme = new ItemIdentificationScheme("999", "Estándar de adopción del contribuyente", ""),
                    PriceAmount = 100_000m, BaseQuantity = 1m, TaxSubtotals = [iva19],
                },
            ],
        };
    }
}
