using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using ImpiFact.Application.Abstractions;
using ImpiFact.Application.Abstractions.Email;
using ImpiFact.Application.Abstractions.Identity;
using ImpiFact.Application.Abstractions.Messaging;
using ImpiFact.Application.Abstractions.Protection;
using ImpiFact.Application.Features.Identity;
using ImpiFact.Application.Features.Identity.PasswordReset;
using Microsoft.Extensions.Logging.Abstractions;

namespace ImpiFact.Application.Tests.Features.Identity;

/// <summary>
/// Recuperación de contraseña con dobles en memoria: que pedir el enlace haga el mismo trabajo
/// exista o no la cuenta, que confirmar revise vencimiento, uso y revocación con un solo error, y que el correo lleve el
/// enlace correcto solo mientras el token sirve.
/// </summary>
[TestFixture]
public sealed class PasswordResetHandlerTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

    private static readonly MailServer Platform = new("smtp.platform.test", 25, null, null, MailEncryption.None, "no-reply@platform.test", "ImpiFact");

    private FakeClock _clock = null!;
    private FakeUserStore _users = null!;
    private FakeResetTokenStore _resetTokens = null!;
    private CountingResetTokenFactory _factory = null!;
    private ReversibleProtector _secrets = null!;
    private RecordingEmailQueue _outbox = null!;
    private FakeRefreshTokenStore _refresh = null!;

    [SetUp]
    public void CreateFakes()
    {
        _clock = new FakeClock();
        _users = new FakeUserStore();
        _resetTokens = new FakeResetTokenStore();
        _factory = new CountingResetTokenFactory();
        _secrets = new ReversibleProtector();
        _outbox = new RecordingEmailQueue();
        _refresh = new FakeRefreshTokenStore();
    }

    [Test]
    public async Task An_unknown_or_inactive_email_does_the_same_token_work_and_gets_the_same_answer_without_writes()
    {
        _users.AddUser("inactivo@example.com", isActive: false);
        var active = _users.AddUser("ana@example.com");

        var unknown = await Request("nadie@example.com");
        var inactive = await Request("inactivo@example.com");
        var known = await Request("  ANA@example.com ");

        Assert.Multiple(() =>
        {
            Assert.That(new[] { unknown.Value, inactive.Value }, Is.All.EqualTo(known.Value));
            Assert.That(_factory.Created, Is.EqualTo(3), "Every request creates a token, so the time spent looks alike.");
            Assert.That(_secrets.Protected, Is.EqualTo(3));
            Assert.That(_resetTokens.Tokens.Select(token => token.UserId), Is.EqualTo(new[] { active.Id }));
            Assert.That(_outbox.Messages.Select(message => message.ResetTokenId), Is.EqualTo(_resetTokens.Tokens.Select(token => token.Id)));
        });
    }

    [Test]
    public async Task A_request_stores_only_the_hash_revokes_the_unused_tokens_and_queues_the_encrypted_token()
    {
        _users.AddUser("ana@example.com");

        await Request("ana@example.com");
        await Request("ana@example.com");

        var sent = _outbox.Messages[1];
        var latest = _resetTokens.Tokens[1];
        Assert.Multiple(() =>
        {
            Assert.That(_resetTokens.Tokens[0].RevokedAt, Is.EqualTo(_clock.UtcNow), "The first link stops working.");
            Assert.That(latest.RevokedAt, Is.Null);
            Assert.That(latest.TokenHash, Is.EqualTo("sha:reset-2"));
            Assert.That(latest.ExpiresAt, Is.EqualTo(_clock.UtcNow.AddMinutes(30)));
            Assert.That(latest.RequestedIp, Is.EqualTo("203.0.113.7"));
            Assert.That(sent.ResetTokenId, Is.EqualTo(latest.Id));
            Assert.That(sent.TokenCipher, Is.EqualTo($"enc({PasswordResetEmail.SecretPurpose}):reset-2"));
            Assert.That(_users.ChangedPasswords, Is.Empty);
        });
    }

    [Test]
    public async Task Confirming_changes_the_password_spends_the_token_and_revokes_every_session()
    {
        var user = _users.AddUser("ana@example.com");
        var token = _resetTokens.Add(user.Id, "sha:abc", _clock.UtcNow.AddMinutes(10));

        var result = await Confirm("abc", "Nueva-Clave-123");
        var again = await Confirm("abc", "Nueva-Clave-456");

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(_users.ChangedPasswords[user.Id], Is.EqualTo("hash:Nueva-Clave-123"));
            Assert.That(_resetTokens.Tokens.Single().UsedAt, Is.EqualTo(_clock.UtcNow));
            Assert.That(_refresh.RevokedAllFor, Is.EqualTo(new[] { (user.Id, _clock.UtcNow) }));
            Assert.That(again.Error, Is.EqualTo(IdentityErrors.InvalidResetToken), "Single use.");
            Assert.That(token.Id, Is.EqualTo(_resetTokens.Tokens.Single().Id));
        });
    }

    [TestCase("expired")]
    [TestCase("used")]
    [TestCase("revoked")]
    [TestCase("unknown")]
    [TestCase("inactive-user")]
    public async Task An_unusable_token_gets_the_same_error_and_changes_nothing(string state)
    {
        var user = _users.AddUser("ana@example.com", isActive: state != "inactive-user");
        var record = _resetTokens.Add(user.Id, "sha:abc", state == "expired" ? _clock.UtcNow : _clock.UtcNow.AddMinutes(10));
        _resetTokens.Tokens[0] = state switch
        {
            "used" => record with { UsedAt = _clock.UtcNow.AddMinutes(-1) },
            "revoked" => record with { RevokedAt = _clock.UtcNow.AddMinutes(-1) },
            _ => record,
        };

        var result = await Confirm(state == "unknown" ? "otro" : "abc", "Nueva-Clave-123");

        Assert.Multiple(() =>
        {
            Assert.That(result.Error, Is.EqualTo(IdentityErrors.InvalidResetToken));
            Assert.That(_users.ChangedPasswords, Is.Empty);
            Assert.That(_refresh.RevokedAllFor, Is.Empty);
        });
    }

    [Test]
    public void The_new_password_follows_the_user_password_policy()
    {
        var validator = new ConfirmPasswordResetValidator();

        var tooShort = validator.Validate(new ConfirmPasswordResetCommand("abc", "corta"));
        var tooLong = validator.Validate(new ConfirmPasswordResetCommand("abc", new string('a', 257)));
        var valid = validator.Validate(new ConfirmPasswordResetCommand("abc", "Nueva-Clave-123"));

        Assert.Multiple(() =>
        {
            Assert.That(tooShort.Errors.Select(error => (error.PropertyName, error.ErrorMessage)),
                Is.EqualTo(new[] { ("new_password", "La contraseña debe tener al menos 10 caracteres.") }));
            Assert.That(tooLong.Errors.Select(error => error.ErrorMessage), Is.EqualTo(new[] { "La contraseña admite hasta 256 caracteres." }));
            Assert.That(valid.IsValid, Is.True);
            Assert.That(new ConfirmPasswordResetCommand("secreto", "Nueva-Clave-123").ToString(), Does.Not.Contain("secreto").And.Not.Contain("Nueva"));
            Assert.That(new RequestPasswordResetCommand("ana@example.com", "1.2.3.4").ToString(), Does.Not.Contain("ana"));
        });
    }

    [TestCase("a\u0000b@example.com")]
    [TestCase("ana\u0001@example.com")]
    public void The_request_email_rejects_control_characters(string email)
    {
        var validator = new RequestPasswordResetValidator();

        var withControl = validator.Validate(new RequestPasswordResetCommand(email, null));
        var valid = validator.Validate(new RequestPasswordResetCommand("ana@example.com", null));

        Assert.Multiple(() =>
        {
            Assert.That(withControl.Errors.Select(error => (error.PropertyName, error.ErrorMessage)),
                Is.EqualTo(new[] { ("email", "El correo no admite caracteres de control.") }));
            Assert.That(valid.IsValid, Is.True);
        });
    }

    [Test]
    public async Task The_email_carries_the_panel_link_with_the_decrypted_token_through_the_platform_server()
    {
        var user = _users.AddUser("ana@example.com");
        var record = _resetTokens.Add(user.Id, "sha:abc", _clock.UtcNow.AddMinutes(30));
        _resetTokens.Recipients[record.Id] = ("ana@example.com", "Ana <Pérez>", true);
        var sender = new RecordingSender();

        await EmailHandler(sender).HandleAsync(EmailMessageFor(record.Id, $"enc({PasswordResetEmail.SecretPurpose}):abc"), CancellationToken.None);

        var (server, email) = sender.Sent.Single();
        Assert.Multiple(() =>
        {
            Assert.That(server, Is.SameAs(Platform));
            Assert.That(email.To, Is.EqualTo(new[] { "ana@example.com" }));
            Assert.That(email.Subject, Is.EqualTo("Restablezca su contraseña de ImpiFact"));
            Assert.That(email.TextBody, Is.EqualTo(
                "Hola, Ana <Pérez>:\n\n" +
                "Recibimos una solicitud para restablecer la contraseña de su cuenta en ImpiFact. Para elegir una contraseña nueva, abra este enlace:\n\n" +
                "https://panel.impifact.test/restablecer?token=abc\n\n" +
                "El enlace vence en 30 minutos y solo se puede usar una vez. Al cambiar la contraseña se cerrarán todas sus sesiones abiertas.\n\n" +
                "Si usted no pidió este cambio, ignore este correo: su contraseña sigue igual."));
            Assert.That(email.HtmlBody, Does.Contain("Hola, Ana &lt;P&#233;rez&gt;:"), "The name is HTML-escaped.");
            Assert.That(email.HtmlBody, Does.Contain("<a href=\"https://panel.impifact.test/restablecer?token=abc\">"));
        });
    }

    [TestCase("used")]
    [TestCase("revoked")]
    [TestCase("expired")]
    [TestCase("inactive-user")]
    [TestCase("missing")]
    public async Task No_email_goes_out_for_a_token_that_no_longer_works(string state)
    {
        var record = _resetTokens.Add(Guid.CreateVersion7(), "sha:abc", state == "expired" ? _clock.UtcNow : _clock.UtcNow.AddMinutes(30));
        _resetTokens.Tokens[0] = state switch
        {
            "used" => record with { UsedAt = _clock.UtcNow },
            "revoked" => record with { RevokedAt = _clock.UtcNow },
            _ => record,
        };
        if (state != "missing")
        {
            _resetTokens.Recipients[record.Id] = ("ana@example.com", "Ana", state != "inactive-user");
        }

        var sender = new RecordingSender();

        await EmailHandler(sender).HandleAsync(EmailMessageFor(record.Id, "enc(password-reset.token):abc"), CancellationToken.None);

        Assert.That(sender.Sent, Is.Empty);
    }

    [Test]
    public void A_token_that_cannot_be_decrypted_fails_the_message_without_retries()
    {
        var record = _resetTokens.Add(Guid.CreateVersion7(), "sha:abc", _clock.UtcNow.AddMinutes(30));
        _resetTokens.Recipients[record.Id] = ("ana@example.com", "Ana", true);

        Assert.ThrowsAsync<PermanentOutboxFailureException>(() =>
            EmailHandler(new RecordingSender()).HandleAsync(EmailMessageFor(record.Id, "enc(otro-proposito):abc"), CancellationToken.None));
    }

    private Task<ImpiFact.Application.Abstractions.Results.Result<PasswordResetRequested>> Request(string email) =>
        new RequestPasswordResetHandler(_users, _resetTokens, _factory, _secrets, _outbox, _clock, NullLogger<RequestPasswordResetHandler>.Instance)
            .HandleAsync(new RequestPasswordResetCommand(email, "203.0.113.7"), CancellationToken.None);

    private Task<ImpiFact.Application.Abstractions.Results.Result<PasswordResetConfirmed>> Confirm(string token, string password) =>
        new ConfirmPasswordResetHandler(
                _resetTokens, _factory, _users, new FakePasswordHasher(), _refresh, _clock, NullLogger<ConfirmPasswordResetHandler>.Instance)
            .HandleAsync(new ConfirmPasswordResetCommand(token, password), CancellationToken.None);

    private SendPasswordResetEmailHandler EmailHandler(RecordingSender sender) =>
        new(_resetTokens, _secrets, new FixedPanelLinks(), new FixedPlatformServer(), sender, _clock, NullLogger<SendPasswordResetEmailHandler>.Instance);

    private static OutboxMessageContext EmailMessageFor(Guid tokenId, string cipher) => new(
        Guid.CreateVersion7(),
        null,
        OutboxMessageTypes.SendPasswordResetEmail,
        JsonSerializer.SerializeToNode(new SendPasswordResetEmailPayload(tokenId, cipher), Json) ?? new JsonObject(),
        1,
        Json);

    private sealed class FakeResetTokenStore : IPasswordResetTokenStore
    {
        public List<PasswordResetTokenRecord> Tokens { get; } = [];

        public Dictionary<Guid, (string Email, string Name, bool Active)> Recipients { get; } = [];

        public PasswordResetTokenRecord Add(Guid userId, string hash, DateTimeOffset expiresAt)
        {
            var record = new PasswordResetTokenRecord(Guid.CreateVersion7(), userId, hash, expiresAt, null, null, null);
            Tokens.Add(record);
            return record;
        }

        public Task AddAsync(PasswordResetTokenRecord token, CancellationToken ct)
        {
            Tokens.Add(token);
            return Task.CompletedTask;
        }

        public Task RevokeUnusedForUserAsync(Guid userId, DateTimeOffset revokedAt, CancellationToken ct)
        {
            for (var i = 0; i < Tokens.Count; i++)
            {
                if (Tokens[i] is { UsedAt: null, RevokedAt: null } token && token.UserId == userId)
                {
                    Tokens[i] = token with { RevokedAt = revokedAt };
                }
            }

            return Task.CompletedTask;
        }

        public Task<PasswordResetTokenRecord?> FindByHashAsync(string tokenHash, CancellationToken ct) =>
            Task.FromResult(Tokens.FirstOrDefault(token => token.TokenHash == tokenHash));

        public Task<bool> MarkUsedAsync(Guid tokenId, DateTimeOffset usedAt, CancellationToken ct)
        {
            var index = Tokens.FindIndex(token => token.Id == tokenId);
            if (index < 0 || Tokens[index].UsedAt is not null || Tokens[index].RevokedAt is not null)
            {
                return Task.FromResult(false);
            }

            Tokens[index] = Tokens[index] with { UsedAt = usedAt };
            return Task.FromResult(true);
        }

        public Task<PasswordResetRecipient?> FindRecipientAsync(Guid tokenId, CancellationToken ct) =>
            Task.FromResult(Tokens.FirstOrDefault(token => token.Id == tokenId) is { } token && Recipients.TryGetValue(tokenId, out var who)
                ? new PasswordResetRecipient(token, who.Email, who.Name, who.Active)
                : null);
    }

    /// <summary>Tokens predecibles: <c>reset-1</c>, <c>reset-2</c>…; el resumen es <c>sha:valor</c>.</summary>
    private sealed class CountingResetTokenFactory : IPasswordResetTokenFactory
    {
        public int Created { get; private set; }

        public IssuedPasswordResetToken Create()
        {
            var value = $"reset-{++Created}";
            return new IssuedPasswordResetToken(value, Hash(value));
        }

        public string Hash(string token) => "sha:" + token;
    }

    /// <summary>«Cifra» como <c>enc(propósito):texto</c> y descifra solo con el mismo propósito.</summary>
    private sealed class ReversibleProtector : ISecretProtector
    {
        public int Protected { get; private set; }

        public string Protect(string purpose, string plain)
        {
            Protected++;
            return $"enc({purpose}):{plain}";
        }

        public string Unprotect(string purpose, string cipher) =>
            cipher.StartsWith($"enc({purpose}):", StringComparison.Ordinal)
                ? cipher[$"enc({purpose}):".Length..]
                : throw new CryptographicException("Wrong purpose.");
    }

    private sealed class RecordingEmailQueue : IPasswordResetEmailQueue
    {
        public List<SendPasswordResetEmailPayload> Messages { get; } = [];

        public Task EnqueueAsync(SendPasswordResetEmailPayload payload, CancellationToken ct)
        {
            Messages.Add(payload);
            return Task.CompletedTask;
        }
    }

    private sealed class FixedPanelLinks : IPanelLinks
    {
        public Uri PasswordReset(string token) => new($"https://panel.impifact.test/restablecer?token={Uri.EscapeDataString(token)}");
    }

    private sealed class FixedPlatformServer : IPlatformMailServer
    {
        public MailServer Default => Platform;
    }

    private sealed class RecordingSender : IEmailSender
    {
        public List<(MailServer Server, EmailMessage Message)> Sent { get; } = [];

        public Task SendAsync(MailServer server, EmailMessage message, CancellationToken ct)
        {
            Sent.Add((server, message));
            return Task.CompletedTask;
        }
    }
}
