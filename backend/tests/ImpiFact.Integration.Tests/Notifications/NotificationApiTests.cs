using System.Globalization;
using System.Net;
using System.Text.Json;
using Dapper;
using ImpiFact.Application.Abstractions;
using ImpiFact.Application.Abstractions.Handlers;
using ImpiFact.Application.Abstractions.Identity;
using ImpiFact.Application.Features.Notifications;
using ImpiFact.Application.Features.Notifications.ListNotifications;
using ImpiFact.Application.Features.Notifications.MarkRead;
using ImpiFact.Domain.Companies;
using ImpiFact.Domain.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace ImpiFact.Integration.Tests.Notifications;

/// <summary>
/// Campana del panel por la API y con PostgreSQL: <c>GET /api/v1/notifications</c> pagina por cursor del más reciente
/// al más antiguo y cuenta los sin leer; marcar uno o todos como leídos es idempotente y nunca toca los avisos de otra
/// empresa ni los dirigidos a otro usuario.
/// </summary>
[Category("Integration")]
public sealed class NotificationApiTests
{
    private static readonly DateTimeOffset Start = new(2026, 1, 10, 15, 0, 0, TimeSpan.Zero);
    private static int sequence;

    [Test]
    public async Task List_walks_every_notification_newest_first_with_the_cursor_and_never_shows_another_company()
    {
        var company = await CompanyAsync();
        var other = await CompanyAsync();
        var expected = new List<(DateTimeOffset CreatedAt, Guid Id)>();
        for (var minute = 1; minute <= 4; minute++)
        {
            var at = Start.AddMinutes(minute);
            expected.Add((at, await NotificationAsync(company, at, isRead: minute % 2 == 0)));
        }

        // Dos avisos en el mismo instante: el id desempata el orden.
        expected.Add((Start.AddMinutes(2), await NotificationAsync(company, Start.AddMinutes(2))));
        await NotificationAsync(other, Start.AddMinutes(30));
        using var client = await ClientAsync(company);

        var seen = new List<Guid>();
        var unreadCounts = new List<long>();
        string? cursor = null;
        var pages = 0;
        do
        {
            using var json = await GetJsonAsync(client, "/api/v1/notifications?limit=2" + (cursor is null ? "" : "&cursor=" + Uri.EscapeDataString(cursor)));
            var items = json.RootElement.GetProperty("items").EnumerateArray().ToList();
            Assert.That(items, Has.Count.LessThanOrEqualTo(2));
            seen.AddRange(items.Select(item => item.GetProperty("id").GetGuid()));
            unreadCounts.Add(json.RootElement.GetProperty("unread_count").GetInt64());
            cursor = json.RootElement.GetProperty("next_cursor").ValueKind == JsonValueKind.Null ? null : json.RootElement.GetProperty("next_cursor").GetString();
            pages++;
        }
        while (cursor is not null && pages < 10);

        // PostgreSQL ordena los uuid por sus bytes, que es el orden del texto en hexadecimal.
        var newestFirst = expected.OrderByDescending(row => row.CreatedAt)
            .ThenByDescending(row => row.Id.ToString("N"), StringComparer.Ordinal)
            .Select(row => row.Id)
            .ToList();
        Assert.Multiple(() =>
        {
            Assert.That(seen, Is.EqualTo(newestFirst), "Every notification once, newest first, none of the other company.");
            Assert.That(pages, Is.EqualTo(3));
            Assert.That(unreadCounts, Is.All.EqualTo(3), "The unread count does not depend on the page.");
        });
    }

    [Test]
    public async Task An_item_has_its_fields_in_snake_case()
    {
        var company = await CompanyAsync();
        var id = await NotificationAsync(company, Start, type: "certificate_expiry", title: "Certificado por vencer", body: "Vence en 30 días.");
        using var client = await ClientAsync(company);

        using var json = await GetJsonAsync(client, "/api/v1/notifications");

        var item = json.RootElement.GetProperty("items").EnumerateArray().Single();
        Assert.Multiple(() =>
        {
            Assert.That(item.GetProperty("id").GetGuid(), Is.EqualTo(id));
            Assert.That(item.GetProperty("type").GetString(), Is.EqualTo("certificate_expiry"));
            Assert.That(item.GetProperty("title").GetString(), Is.EqualTo("Certificado por vencer"));
            Assert.That(item.GetProperty("body").GetString(), Is.EqualTo("Vence en 30 días."));
            Assert.That(item.GetProperty("is_read").GetBoolean(), Is.False);
            Assert.That(item.GetProperty("created_at").GetDateTimeOffset(), Is.EqualTo(Start));
            Assert.That(json.RootElement.GetProperty("next_cursor").ValueKind, Is.EqualTo(JsonValueKind.Null));
            Assert.That(json.RootElement.GetProperty("unread_count").GetInt64(), Is.EqualTo(1));
        });
    }

    [Test]
    public async Task The_unread_filter_returns_only_unread_or_only_read_and_keeps_the_total_unread_count()
    {
        var company = await CompanyAsync();
        var unread = await NotificationAsync(company, Start.AddMinutes(1));
        var read = await NotificationAsync(company, Start.AddMinutes(2), isRead: true);
        using var client = await ClientAsync(company);

        using var onlyUnread = await GetJsonAsync(client, "/api/v1/notifications?unread=true");
        using var onlyRead = await GetJsonAsync(client, "/api/v1/notifications?unread=false");

        Assert.Multiple(() =>
        {
            Assert.That(Ids(onlyUnread), Is.EqualTo(new[] { unread }));
            Assert.That(Ids(onlyRead), Is.EqualTo(new[] { read }));
            Assert.That(onlyUnread.RootElement.GetProperty("unread_count").GetInt64(), Is.EqualTo(1));
            Assert.That(onlyRead.RootElement.GetProperty("unread_count").GetInt64(), Is.EqualTo(1));
        });
    }

    [Test]
    public async Task A_notification_for_one_user_is_shown_only_to_that_user()
    {
        var company = await CompanyAsync();
        var owner = await UserAsync();
        var colleague = await UserAsync();
        var everyone = await NotificationAsync(company, Start.AddMinutes(1));
        var forOwner = await NotificationAsync(company, Start.AddMinutes(2), userId: owner);
        using var client = await ClientAsync(company);

        using var byKey = await GetJsonAsync(client, "/api/v1/notifications");
        var ownerPage = await ListAsUserAsync(company, owner);
        var colleaguePage = await ListAsUserAsync(company, colleague);
        var colleagueMark = await MarkAsUserAsync(company, colleague, forOwner);

        Assert.Multiple(() =>
        {
            Assert.That(Ids(byKey), Is.EqualTo(new[] { everyone }), "An API key has no user: only company-wide notices.");
            Assert.That(byKey.RootElement.GetProperty("unread_count").GetInt64(), Is.EqualTo(1));
            Assert.That(ownerPage.Items.Select(item => item.Id), Is.EqualTo(new[] { forOwner, everyone }));
            Assert.That(ownerPage.UnreadCount, Is.EqualTo(2));
            Assert.That(colleaguePage.Items.Select(item => item.Id), Is.EqualTo(new[] { everyone }));
            Assert.That(colleagueMark.Error.Code, Is.EqualTo("notification.not_found"));
        });
        Assert.That(await IsReadAsync(forOwner), Is.False);
    }

    [Test]
    public async Task Marking_one_as_read_is_idempotent_and_stamps_the_change_with_the_application_clock()
    {
        var company = await CompanyAsync();
        var id = await NotificationAsync(company, Start);
        var untouched = await NotificationAsync(company, Start.AddMinutes(1));
        using var client = await ClientAsync(company);

        using var first = await client.PostAsync($"/api/v1/notifications/{id}/read", null);
        var afterFirst = await RowAsync(id);
        using var second = await client.PostAsync($"/api/v1/notifications/{id}/read", null);
        var afterSecond = await RowAsync(id);
        using var list = await GetJsonAsync(client, "/api/v1/notifications");

        Assert.Multiple(() =>
        {
            Assert.That(first.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
            Assert.That(second.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
            Assert.That(afterFirst.IsRead, Is.True);
            Assert.That(afterFirst.UpdatedAt, Is.EqualTo(NotificationsHost.Api.Clock.UtcNow));
            Assert.That(afterFirst.RowVersion, Is.EqualTo(2));
            Assert.That(afterSecond, Is.EqualTo(afterFirst), "A second mark changes nothing.");
            Assert.That(list.RootElement.GetProperty("unread_count").GetInt64(), Is.EqualTo(1));
        });
        Assert.That(await IsReadAsync(untouched), Is.False);
    }

    [Test]
    public async Task Another_companys_notification_is_not_found_and_stays_unread()
    {
        var company = await CompanyAsync();
        var other = await CompanyAsync();
        var foreign = await NotificationAsync(other, Start);
        using var client = await ClientAsync(company);

        using var mark = await client.PostAsync($"/api/v1/notifications/{foreign}/read", null);
        using var unknown = await client.PostAsync($"/api/v1/notifications/{Guid.CreateVersion7()}/read", null);
        var body = await mark.Content.ReadAsStringAsync();

        Assert.Multiple(() =>
        {
            Assert.That(mark.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
            Assert.That(unknown.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
            Assert.That(body, Does.Contain("notification.not_found"));
        });
        Assert.That(await IsReadAsync(foreign), Is.False);
    }

    [Test]
    public async Task Read_all_marks_only_the_visible_notifications_of_the_own_company_and_repeating_it_marks_none()
    {
        var company = await CompanyAsync();
        var other = await CompanyAsync();
        var user = await UserAsync();
        var first = await NotificationAsync(company, Start.AddMinutes(1));
        var second = await NotificationAsync(company, Start.AddMinutes(2));
        var alreadyRead = await NotificationAsync(company, Start.AddMinutes(3), isRead: true);
        var forUser = await NotificationAsync(company, Start.AddMinutes(4), userId: user);
        var foreign = await NotificationAsync(other, Start.AddMinutes(5));
        using var client = await ClientAsync(company);

        using var once = await PostJsonAsync(client, "/api/v1/notifications/read-all");
        using var twice = await PostJsonAsync(client, "/api/v1/notifications/read-all");
        using var list = await GetJsonAsync(client, "/api/v1/notifications");
        var firstRow = await RowAsync(first);

        Assert.Multiple(() =>
        {
            Assert.That(once.RootElement.GetProperty("marked_count").GetInt32(), Is.EqualTo(2));
            Assert.That(twice.RootElement.GetProperty("marked_count").GetInt32(), Is.EqualTo(0));
            Assert.That(list.RootElement.GetProperty("unread_count").GetInt64(), Is.EqualTo(0));
            Assert.That(firstRow.UpdatedAt, Is.EqualTo(NotificationsHost.Api.Clock.UtcNow));
            Assert.That(firstRow.RowVersion, Is.EqualTo(2));
        });
        Assert.Multiple(async () =>
        {
            Assert.That(await IsReadAsync(second), Is.True);
            Assert.That((await RowAsync(alreadyRead)).RowVersion, Is.EqualTo(1), "An already read notice is not rewritten.");
            Assert.That(await IsReadAsync(forUser), Is.False, "A notice for one user is not the API key's to mark.");
            Assert.That(await IsReadAsync(foreign), Is.False, "Another company's notice is never touched.");
        });
    }

    [Test]
    public async Task The_list_needs_documents_read_and_rejects_a_malformed_cursor()
    {
        var company = await CompanyAsync();
        using var writeOnly = await ClientAsync(company, ApiScopes.DocumentsWrite);
        using var reader = await ClientAsync(company);

        using var forbidden = await writeOnly.GetAsync("/api/v1/notifications");
        using var markForbidden = await writeOnly.PostAsync("/api/v1/notifications/read-all", null);
        using var badCursor = await reader.GetAsync("/api/v1/notifications?cursor=%3F%3F%3F");
        using var badLimit = await reader.GetAsync("/api/v1/notifications?limit=101");

        Assert.Multiple(() =>
        {
            Assert.That(forbidden.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
            Assert.That(markForbidden.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
            Assert.That((int)badCursor.StatusCode, Is.EqualTo(422));
            Assert.That((int)badLimit.StatusCode, Is.EqualTo(422));
        });
    }

    [Test]
    public async Task A_platform_key_without_company_cannot_list_notifications()
    {
        using var platform = await NotificationsHost.Api.ClientWithNewKeyAsync(null, ApiScopes.Platform);

        using var response = await platform.GetAsync("/api/v1/notifications");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
    }

    private static async Task<NotificationListResponse> ListAsUserAsync(Guid companyId, Guid userId)
    {
        await using var scope = NotificationsHost.Api.OpenScope(companyId, userId, ActorType.User);
        var handler = scope.Services.GetRequiredService<IHandler<ListNotificationsQuery, NotificationListResponse>>();
        return (await handler.HandleAsync(new ListNotificationsQuery(null, null, null), CancellationToken.None)).Value;
    }

    private static async Task<Application.Abstractions.Results.Result<NotificationsMarkedResponse>> MarkAsUserAsync(
        Guid companyId, Guid userId, Guid notificationId)
    {
        await using var scope = NotificationsHost.Api.OpenScope(companyId, userId, ActorType.User);
        var handler = scope.Services.GetRequiredService<IHandler<MarkNotificationReadCommand, NotificationsMarkedResponse>>();
        return await handler.HandleAsync(new MarkNotificationReadCommand(notificationId), CancellationToken.None);
    }

    private static IReadOnlyList<Guid> Ids(JsonDocument json) =>
        json.RootElement.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("id").GetGuid()).ToList();

    private static Task<HttpClient> ClientAsync(Guid companyId, params string[] scopes) =>
        NotificationsHost.Api.ClientWithNewKeyAsync(companyId, scopes.Length == 0 ? [ApiScopes.DocumentsRead] : scopes);

    private static async Task<JsonDocument> GetJsonAsync(HttpClient client, string url)
    {
        using var response = await client.GetAsync(url);
        var body = await response.Content.ReadAsStringAsync();
        Assert.That((int)response.StatusCode, Is.EqualTo(200), body);
        return JsonDocument.Parse(body);
    }

    private static async Task<JsonDocument> PostJsonAsync(HttpClient client, string url)
    {
        using var response = await client.PostAsync(url, null);
        var body = await response.Content.ReadAsStringAsync();
        Assert.That((int)response.StatusCode, Is.EqualTo(200), body);
        return JsonDocument.Parse(body);
    }

    /// <summary>Una empresa nueva con un NIT (Número de Identificación Tributaria) que no choca con otras pruebas.</summary>
    private static async Task<Guid> CompanyAsync()
    {
        var id = Guid.CreateVersion7();
        var nit = (810_000_000 + Interlocked.Increment(ref sequence)).ToString(CultureInfo.InvariantCulture);
        await using var scope = NotificationsHost.Api.OpenScope(null, null, ActorType.System);
        scope.Db.Add(new Company
        {
            Id = id,
            IdentificationNumber = nit,
            Dv = "1",
            Name = "EMPRESA " + nit + " SAS",
            Email = $"company{nit}@example.com",
            Phone = "3000000000",
            Address = "Calle 1",
            LanguageId = 79,
            CountryId = 46,
            MunicipalityId = 149,
            TaxId = 1,
            TypeCurrencyId = 35,
            TypeOperationId = 10,
            TypeDocumentIdentificationId = 6,
            TypeOrganizationId = 1,
            TypeRegimeId = 1,
        });
        await scope.Db.SaveChangesAsync();
        return id;
    }

    private static async Task<Guid> UserAsync()
    {
        await using var scope = NotificationsHost.Api.OpenScope(null, null, ActorType.System);
        var user = new User
        {
            Email = $"notify-{Guid.CreateVersion7():N}@example.com",
            PasswordHash = "not-a-real-hash",
            Name = "Usuario de prueba",
            SecurityStamp = Guid.NewGuid().ToString("N"),
        };
        scope.Db.Set<User>().Add(user);
        await scope.Db.SaveChangesAsync();
        return user.Id;
    }

    /// <summary>Inserta un aviso creado en <paramref name="createdAt"/> (UTC) con SQL directo, para fijar su fecha.</summary>
    private static async Task<Guid> NotificationAsync(
        Guid companyId,
        DateTimeOffset createdAt,
        bool isRead = false,
        Guid? userId = null,
        string type = "document_unconfirmed",
        string title = "Documento sin confirmar",
        string body = "La DIAN no confirmó el documento.")
    {
        var id = Guid.CreateVersion7();
        await using var connection = TestDatabase.CreateConnection(NotificationsHost.ConnectionString);
        await connection.ExecuteAsync(
            """
            insert into notifications (id, company_id, user_id, type, title, body, is_read, created_at, updated_at, row_version)
            values (@Id, @CompanyId, @UserId, @Type, @Title, @Body, @IsRead, @CreatedAt, @CreatedAt, 1)
            """,
            new { Id = id, CompanyId = companyId, UserId = userId, Type = type, Title = title, Body = body, IsRead = isRead, CreatedAt = createdAt.ToUniversalTime() });
        return id;
    }

    private static async Task<bool> IsReadAsync(Guid id) => (await RowAsync(id)).IsRead;

    private static async Task<StoredNotification> RowAsync(Guid id)
    {
        await using var connection = TestDatabase.CreateConnection(NotificationsHost.ConnectionString);
        var row = await connection.QuerySingleAsync<(bool IsRead, DateTime UpdatedAt, long RowVersion)>(
            "select is_read, updated_at, row_version from notifications where id = @Id", new { Id = id });
        return new StoredNotification(row.IsRead, new DateTimeOffset(DateTime.SpecifyKind(row.UpdatedAt, DateTimeKind.Utc)), row.RowVersion);
    }

    private sealed record StoredNotification(bool IsRead, DateTimeOffset UpdatedAt, long RowVersion);
}
