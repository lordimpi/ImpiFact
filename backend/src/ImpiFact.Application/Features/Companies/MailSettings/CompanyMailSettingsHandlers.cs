using System.Text.Json.Serialization;
using FluentValidation;
using ImpiFact.Application.Abstractions;
using ImpiFact.Application.Abstractions.Companies;
using ImpiFact.Application.Abstractions.Email;
using ImpiFact.Application.Abstractions.Handlers;
using ImpiFact.Application.Abstractions.Protection;
using ImpiFact.Application.Abstractions.Results;
using ImpiFact.Domain.Companies;

namespace ImpiFact.Application.Features.Companies.MailSettings;

// Own mail server (SMTP, the email sending protocol) of the company: RF-01 «correo», the GET emailconfig of ApiDian. The
// row of company_mail_settings is the one MailServerSelector reads when the Worker sends the email of a document. The
// password is write-only: it is stored encrypted with ISecretProtector and the SMTP purpose, and never returned or logged
// (php-rules, section 7, bug #5).

/// <summary><c>GET /api/v1/companies/{nit}/mail-settings</c>.</summary>
public sealed record GetCompanyMailSettingsQuery(string? Nit);

/// <summary><c>DELETE /api/v1/companies/{nit}/mail-settings</c>: the company goes back to the platform server.</summary>
public sealed record DeleteCompanyMailSettingsCommand(string? Nit) : ICommand;

/// <summary><c>PUT /api/v1/companies/{nit}/mail-settings</c>: creates or replaces the own server of the company.</summary>
public sealed record SaveCompanyMailSettingsCommand : ICommand
{
    [JsonIgnore]
    public string? Nit { get; init; }

    /// <summary>Host name or IP address of the SMTP server, without scheme.</summary>
    [JsonPropertyName("host")]
    public string? Host { get; init; }

    [JsonPropertyName("port")]
    public int? Port { get; init; }

    [JsonPropertyName("username")]
    public string? Username { get; init; }

    /// <summary>Write-only. Required the first time; without it a new save keeps the stored one.</summary>
    [JsonPropertyName("password")]
    public string? Password { get; init; }

    /// <summary><c>ssl</c> (TLS from the start), <c>tls</c> or <c>starttls</c> (STARTTLS) or <c>none</c>.</summary>
    [JsonPropertyName("encryption")]
    public string? Encryption { get; init; }

    /// <summary>Sender address; without it the email goes out with the sender of the platform.</summary>
    [JsonPropertyName("from_address")]
    public string? FromAddress { get; init; }

    [JsonPropertyName("from_name")]
    public string? FromName { get; init; }

    /// <summary>Keeps the server saved but sends through the platform server (false when absent).</summary>
    [JsonPropertyName("use_platform_default")]
    public bool? UsePlatformDefault { get; init; }

    /// <summary>Leaves the password out, so that an accidental log of the object does not leak it.</summary>
    public override string ToString() => $"SaveCompanyMailSettingsCommand {{ Host = {Host}, Port = {Port}, password hidden }}";
}

/// <summary>The mail server of the company as the panel shows it: everything but the password.</summary>
public sealed record CompanyMailSettingsResponse
{
    public const string CompanyServer = "company";

    public const string PlatformServer = "platform";

    /// <summary>
    /// <c>company</c> when the email goes out through the own server (complete and not overridden by
    /// <c>use_platform_default</c>); <c>platform</c> otherwise.
    /// </summary>
    [JsonPropertyName("in_use")]
    public string InUse { get; init; } = PlatformServer;

    [JsonPropertyName("use_platform_default")]
    public bool UsePlatformDefault { get; init; }

    [JsonPropertyName("host")]
    public string? Host { get; init; }

    [JsonPropertyName("port")]
    public int? Port { get; init; }

    [JsonPropertyName("username")]
    public string? Username { get; init; }

    [JsonPropertyName("encryption")]
    public string? Encryption { get; init; }

    [JsonPropertyName("from_address")]
    public string? FromAddress { get; init; }

    [JsonPropertyName("from_name")]
    public string? FromName { get; init; }

    /// <summary>Whether a password is stored; the password itself is never returned.</summary>
    [JsonPropertyName("has_password")]
    public bool HasPassword { get; init; }

    internal static CompanyMailSettingsResponse From(CompanyMailSettings? row)
    {
        if (row is null)
        {
            return new CompanyMailSettingsResponse();
        }

        var settings = new CompanyMailServerSettings(
            row.UsePlatformDefault, row.Host, row.Port, row.Username, row.PasswordCipher, row.Encryption, row.FromAddress, row.FromName);
        return new CompanyMailSettingsResponse
        {
            InUse = settings.SendsThroughCompany ? CompanyServer : PlatformServer,
            UsePlatformDefault = row.UsePlatformDefault,
            Host = row.Host,
            Port = row.Port,
            Username = row.Username,
            Encryption = row.Encryption,
            FromAddress = row.FromAddress,
            FromName = row.FromName,
            HasPassword = !string.IsNullOrEmpty(row.PasswordCipher),
        };
    }
}

/// <summary>Rules and messages of the mail server fields.</summary>
public static class MailSettingsRules
{
    public const int MinPort = 1;

    public const int MaxPort = 65535;

    /// <summary>Longest text column of <c>company_mail_settings</c>.</summary>
    public const int MaxLength = 255;

    /// <summary>
    /// Longest password: its Data Protection ciphertext (with up to 3 UTF-8 bytes a character) still fits the 512 characters
    /// of <c>password_cipher</c>.
    /// </summary>
    public const int MaxPasswordLength = 100;

    /// <summary>Accepted encryption values, as ApiDian stores them (<c>mail_encryption</c>).</summary>
    public static readonly IReadOnlyList<string> Encryptions = ["ssl", "tls", "starttls", "none"];

    public const string InvalidHost = "host debe ser un nombre de servidor o una dirección IP, sin protocolo ni espacios.";

    public const string InvalidPort = "El campo port debe estar entre 1 y 65535.";

    public const string InvalidEncryption = "encryption debe ser ssl, tls, starttls o none.";

    public const string PasswordRequired = "El campo contraseña es obligatorio para guardar el servidor de correo por primera vez.";

    public static string? Normalize(string? encryption) => encryption?.Trim().ToLowerInvariant();

    internal static bool IsHost(string? host) =>
        host is not null
        && host.Length <= MaxLength
        && host == host.Trim()
        && Uri.CheckHostName(host) is UriHostNameType.Dns or UriHostNameType.IPv4 or UriHostNameType.IPv6;
}

/// <summary>
/// Spanish 422 per field: host (name or IP, no scheme), port (1-65535), username (required), encryption (ssl, tls,
/// starttls or none, without case), from address (an email) and the lengths of the columns. The password is checked only for
/// its length here; whether the first save brings one is checked by the handler.
/// </summary>
internal sealed class SaveCompanyMailSettingsValidator : AbstractValidator<SaveCompanyMailSettingsCommand>
{
    public SaveCompanyMailSettingsValidator()
    {
        RuleFor(command => command.Host)
            .Cascade(CascadeMode.Stop)
            .Must(host => !string.IsNullOrWhiteSpace(host)).WithMessage(PhpMessages.Required("host"))
            .Must(MailSettingsRules.IsHost).WithMessage(MailSettingsRules.InvalidHost)
            .OverridePropertyName("host");
        RuleFor(command => command.Port)
            .Cascade(CascadeMode.Stop)
            .NotNull().WithMessage(PhpMessages.Required("port"))
            .InclusiveBetween(MailSettingsRules.MinPort, MailSettingsRules.MaxPort).WithMessage(MailSettingsRules.InvalidPort)
            .OverridePropertyName("port");
        RuleFor(command => command.Username)
            .Cascade(CascadeMode.Stop)
            .Must(username => !string.IsNullOrWhiteSpace(username)).WithMessage(PhpMessages.Required("username"))
            .MaximumLength(MailSettingsRules.MaxLength).WithMessage(PhpMessages.MaxLength("username", MailSettingsRules.MaxLength))
            .OverridePropertyName("username");
        RuleFor(command => command.Password)
            .Must(password => password is null || password.Length > 0).WithMessage(PhpMessages.Required("password"))
            .MaximumLength(MailSettingsRules.MaxPasswordLength)
            .WithMessage(PhpMessages.MaxLength("password", MailSettingsRules.MaxPasswordLength))
            .OverridePropertyName("password");
        RuleFor(command => command.Encryption)
            .Cascade(CascadeMode.Stop)
            .Must(encryption => !string.IsNullOrWhiteSpace(encryption)).WithMessage(PhpMessages.Required("encryption"))
            .Must(encryption => MailSettingsRules.Encryptions.Contains(MailSettingsRules.Normalize(encryption)))
            .WithMessage(MailSettingsRules.InvalidEncryption)
            .OverridePropertyName("encryption");
        RuleFor(command => command.FromAddress)
            .Cascade(CascadeMode.Stop)
            .MaximumLength(MailSettingsRules.MaxLength).WithMessage(PhpMessages.MaxLength("from_address", MailSettingsRules.MaxLength))
            .Must(EmailAddresses.IsValid).WithMessage(PhpMessages.Email("from_address"))
            .OverridePropertyName("from_address");
        RuleFor(command => command.FromName)
            .MaximumLength(MailSettingsRules.MaxLength).WithMessage(PhpMessages.MaxLength("from_name", MailSettingsRules.MaxLength))
            .OverridePropertyName("from_name");
    }
}

internal sealed class GetCompanyMailSettingsHandler(ICompanyRepository companies, ITenantContext tenant)
    : IHandler<GetCompanyMailSettingsQuery, CompanyMailSettingsResponse>
{
    public async Task<Result<CompanyMailSettingsResponse>> HandleAsync(GetCompanyMailSettingsQuery request, CancellationToken ct)
    {
        var own = await CompanyAccess.OwnCompanyAsync(companies, tenant, request.Nit, ct);
        return own.IsSuccess
            ? CompanyMailSettingsResponse.From(await companies.GetMailSettingsAsync(own.Value.Id, ct))
            : own.Error;
    }
}

/// <summary>
/// Creates or replaces the row of the company. A new password is encrypted for <see cref="SecretPurposes.SmtpPassword"/>;
/// without one, the stored cipher stays, and a first save without password answers 422.
/// </summary>
internal sealed class SaveCompanyMailSettingsHandler(ICompanyRepository companies, ITenantContext tenant, ISecretProtector secrets)
    : IHandler<SaveCompanyMailSettingsCommand, CompanyMailSettingsResponse>
{
    public async Task<Result<CompanyMailSettingsResponse>> HandleAsync(SaveCompanyMailSettingsCommand request, CancellationToken ct)
    {
        var own = await CompanyAccess.OwnCompanyAsync(companies, tenant, request.Nit, ct);
        if (!own.IsSuccess)
        {
            return own.Error;
        }

        var row = await companies.GetMailSettingsAsync(own.Value.Id, ct);
        if (request.Password is null && string.IsNullOrEmpty(row?.PasswordCipher))
        {
            return Error.Validation([new FieldError("password", MailSettingsRules.PasswordRequired)]);
        }

        if (row is null)
        {
            row = new CompanyMailSettings { CompanyId = own.Value.Id };
            companies.Add(row);
        }

        row.UsePlatformDefault = request.UsePlatformDefault ?? false;
        row.Host = request.Host!;
        row.Port = request.Port;
        row.Username = request.Username!.Trim();
        row.Encryption = MailSettingsRules.Normalize(request.Encryption);
        row.FromAddress = string.IsNullOrWhiteSpace(request.FromAddress) ? null : request.FromAddress.Trim();
        row.FromName = string.IsNullOrWhiteSpace(request.FromName) ? null : request.FromName.Trim();
        if (request.Password is not null)
        {
            row.PasswordCipher = secrets.Protect(SecretPurposes.SmtpPassword, request.Password);
        }

        return CompanyMailSettingsResponse.From(row);
    }
}

/// <summary>Deletes the row; without one it answers the same (it is idempotent).</summary>
internal sealed class DeleteCompanyMailSettingsHandler(ICompanyRepository companies, ITenantContext tenant)
    : IHandler<DeleteCompanyMailSettingsCommand, bool>
{
    public async Task<Result<bool>> HandleAsync(DeleteCompanyMailSettingsCommand request, CancellationToken ct)
    {
        var own = await CompanyAccess.OwnCompanyAsync(companies, tenant, request.Nit, ct);
        if (!own.IsSuccess)
        {
            return own.Error;
        }

        if (await companies.GetMailSettingsAsync(own.Value.Id, ct) is { } row)
        {
            companies.Remove(row);
        }

        return true;
    }
}
