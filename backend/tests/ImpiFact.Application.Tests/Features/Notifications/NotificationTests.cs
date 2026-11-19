using ImpiFact.Application.Abstractions;
using ImpiFact.Application.Features.Notifications;
using ImpiFact.Application.Features.Notifications.ListNotifications;
using ImpiFact.Application.Features.Notifications.MarkRead;

namespace ImpiFact.Application.Tests.Features.Notifications;

/// <summary>Campana del panel: listado por cursor con conteo de no leídas y marcado como leídas.</summary>
public sealed class NotificationTests
{
    private static readonly DateTimeOffset Start = new(2026, 3, 1, 12, 0, 0, TimeSpan.Zero);

    private FakeNotificationInbox _inbox = null!;
    private NotificationTenant _tenant = null!;

    [SetUp]
    public void SetUp()
    {
        _inbox = new FakeNotificationInbox();
        _tenant = new NotificationTenant { CompanyId = Guid.CreateVersion7(), UserId = Guid.CreateVersion7(), Actor = ActorType.User };
    }

    [Test]
    public void A_cursor_round_trips_the_key()
    {
        var key = new NotificationKey(Start.AddTicks(1234567), Guid.CreateVersion7());

        Assert.That(NotificationCursor.TryDecode(NotificationCursor.Encode(key), out var decoded), Is.True);
        Assert.That(decoded, Is.EqualTo(key));
    }

    [TestCase("")]
    [TestCase("not a cursor")]
    [TestCase("MTIz")]
    public void A_malformed_cursor_does_not_decode(string cursor)
    {
        Assert.That(NotificationCursor.TryDecode(cursor, out _), Is.False);
    }

    [Test]
    public async Task First_page_asks_for_one_more_row_returns_the_next_cursor_and_the_unread_count()
    {
        var newest = Item(Start.AddMinutes(3));
        var middle = Item(Start.AddMinutes(2));
        var oldest = Item(Start.AddMinutes(1));
        _inbox.Listed.AddRange([newest, middle, oldest]);
        _inbox.Unread = 7;

        var result = await ListHandler().HandleAsync(new ListNotificationsQuery(null, null, 2), CancellationToken.None);

        var filter = _inbox.ListCalls.Single();
        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Value.Items.Select(item => item.Id), Is.EqualTo(new[] { newest.Id, middle.Id }));
            Assert.That(result.Value.UnreadCount, Is.EqualTo(7));
            Assert.That(NotificationCursor.TryDecode(result.Value.NextCursor!, out var next), Is.True);
            Assert.That(next, Is.EqualTo(new NotificationKey(middle.CreatedAt, middle.Id)));
            Assert.That(filter.Audience, Is.EqualTo(new NotificationAudience(_tenant.CompanyId!.Value, _tenant.UserId)));
            Assert.That(filter.Take, Is.EqualTo(3));
            Assert.That(filter.After, Is.Null);
            Assert.That(filter.IsRead, Is.Null);
            Assert.That(_inbox.CountCalls.Single(), Is.EqualTo(filter.Audience));
        });
    }

    [Test]
    public async Task Last_page_has_no_cursor_and_the_cursor_and_default_limit_reach_the_inbox()
    {
        var after = new NotificationKey(Start, Guid.CreateVersion7());
        _inbox.Listed.Add(Item(Start.AddMinutes(-1)));

        var result = await ListHandler().HandleAsync(
            new ListNotificationsQuery(null, NotificationCursor.Encode(after), null), CancellationToken.None);

        var filter = _inbox.ListCalls.Single();
        Assert.Multiple(() =>
        {
            Assert.That(result.Value.Items, Has.Count.EqualTo(1));
            Assert.That(result.Value.NextCursor, Is.Null);
            Assert.That(filter.After, Is.EqualTo(after));
            Assert.That(filter.Take, Is.EqualTo(ListNotificationsQueryValidator.DefaultLimit + 1));
        });
    }

    [TestCase(true, false)]
    [TestCase(false, true)]
    public async Task The_unread_filter_asks_for_the_opposite_read_state(bool unread, bool isRead)
    {
        await ListHandler().HandleAsync(new ListNotificationsQuery(unread, null, null), CancellationToken.None);

        Assert.That(_inbox.ListCalls.Single().IsRead, Is.EqualTo(isRead));
    }

    [Test]
    public async Task An_item_carries_its_fields_and_read_state()
    {
        var item = new NotificationItem(Guid.CreateVersion7(), "certificate_expiry", "Certificado por vencer", "Vence en 30 días.", true, Start);
        _inbox.Listed.Add(item);

        var result = await ListHandler().HandleAsync(new ListNotificationsQuery(null, null, null), CancellationToken.None);

        var response = result.Value.Items.Single();
        Assert.Multiple(() =>
        {
            Assert.That(response.Id, Is.EqualTo(item.Id));
            Assert.That(response.Type, Is.EqualTo("certificate_expiry"));
            Assert.That(response.Title, Is.EqualTo("Certificado por vencer"));
            Assert.That(response.Body, Is.EqualTo("Vence en 30 días."));
            Assert.That(response.IsRead, Is.True);
            Assert.That(response.CreatedAt, Is.EqualTo(Start));
        });
    }

    [TestCase(0, "El campo limit debe ser como mínimo 1.")]
    [TestCase(101, "El campo limit debe ser como máximo 100.")]
    public void A_limit_out_of_range_is_invalid(int limit, string message)
    {
        var result = new ListNotificationsQueryValidator().Validate(new ListNotificationsQuery(null, null, limit));

        Assert.That(result.Errors.Select(error => (error.PropertyName, error.ErrorMessage)), Is.EqualTo(new[] { ("limit", message) }));
    }

    [Test]
    public void A_malformed_cursor_is_invalid()
    {
        var result = new ListNotificationsQueryValidator().Validate(new ListNotificationsQuery(null, "???", null));

        Assert.That(result.Errors.Select(error => (error.PropertyName, error.ErrorMessage)), Is.EqualTo(new[] { ("cursor", "cursor es inválido.") }));
    }

    [Test]
    public async Task Marking_an_unknown_notification_is_not_found()
    {
        var result = await MarkHandler().HandleAsync(new MarkNotificationReadCommand(Guid.CreateVersion7()), CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Error.Code, Is.EqualTo("notification.not_found"));
            Assert.That(result.Error.Type, Is.EqualTo(Abstractions.Results.ErrorType.NotFound));
        });
    }

    [Test]
    public async Task Marking_a_visible_notification_passes_the_audience_and_the_id()
    {
        var id = Guid.CreateVersion7();
        _inbox.Existing.Add(id);

        var result = await MarkHandler().HandleAsync(new MarkNotificationReadCommand(id), CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(_inbox.MarkCalls.Single(), Is.EqualTo((new NotificationAudience(_tenant.CompanyId!.Value, _tenant.UserId), id)));
        });
    }

    [Test]
    public async Task Marking_all_passes_the_audience_and_returns_how_many_changed()
    {
        _tenant.UserId = null;
        _tenant.Actor = ActorType.ApiKey;
        _inbox.MarkedByAll = 4;

        var result = await MarkAllHandler().HandleAsync(new MarkAllNotificationsReadCommand(), CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.Value.MarkedCount, Is.EqualTo(4));
            Assert.That(_inbox.MarkAllCalls.Single(), Is.EqualTo(new NotificationAudience(_tenant.CompanyId!.Value, null)));
        });
    }

    private ListNotificationsHandler ListHandler() => new(_inbox, _tenant);

    private MarkNotificationReadHandler MarkHandler() => new(_inbox, _tenant);

    private MarkAllNotificationsReadHandler MarkAllHandler() => new(_inbox, _tenant);

    private static NotificationItem Item(DateTimeOffset createdAt) =>
        new(Guid.CreateVersion7(), "document_unconfirmed", "Documento sin confirmar", "La DIAN no confirmó el documento.", false, createdAt);
}

internal sealed class NotificationTenant : ITenantContext
{
    public Guid? CompanyId { get; set; }

    public Guid? UserId { get; set; }

    public ActorType Actor { get; set; }

    public IReadOnlySet<string> Scopes { get; set; } = new HashSet<string>();
}

internal sealed class FakeNotificationInbox : INotificationInbox
{
    public List<NotificationItem> Listed { get; } = [];

    public HashSet<Guid> Existing { get; } = [];

    public long Unread { get; set; }

    public int MarkedByAll { get; set; }

    public List<NotificationListFilter> ListCalls { get; } = [];

    public List<NotificationAudience> CountCalls { get; } = [];

    public List<(NotificationAudience Audience, Guid Id)> MarkCalls { get; } = [];

    public List<NotificationAudience> MarkAllCalls { get; } = [];

    public Task<IReadOnlyList<NotificationItem>> ListAsync(NotificationListFilter filter, CancellationToken ct)
    {
        ListCalls.Add(filter);
        return Task.FromResult<IReadOnlyList<NotificationItem>>(Listed.Take(filter.Take).ToList());
    }

    public Task<long> CountUnreadAsync(NotificationAudience audience, CancellationToken ct)
    {
        CountCalls.Add(audience);
        return Task.FromResult(Unread);
    }

    public Task<bool> MarkReadAsync(NotificationAudience audience, Guid notificationId, CancellationToken ct)
    {
        MarkCalls.Add((audience, notificationId));
        return Task.FromResult(Existing.Contains(notificationId));
    }

    public Task<int> MarkAllReadAsync(NotificationAudience audience, CancellationToken ct)
    {
        MarkAllCalls.Add(audience);
        return Task.FromResult(MarkedByAll);
    }
}
