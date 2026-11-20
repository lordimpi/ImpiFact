using ImpiFact.Application.Abstractions.Identity;
using ImpiFact.Application.Features.Identity.Sessions;

namespace ImpiFact.Application.Tests.Features.Identity;

/// <summary>
/// El plazo propio de las revocaciones de sesión (<see cref="SessionRevocation"/>): si se cumple, es una base que no
/// contesta (503); una cancelación que viene de otro lado, como la de una petición abandonada, sigue siendo cancelación.
/// </summary>
[TestFixture]
public sealed class SessionRevocationTests
{
    [Test]
    public async Task A_revocation_that_finishes_in_time_returns_its_result()
    {
        var result = await SessionRevocation.WithinDeadlineAsync(TimeSpan.FromSeconds(5), _ => Task.FromResult(42));

        Assert.That(result, Is.EqualTo(42));
    }

    [Test]
    public void A_revocation_that_runs_out_of_its_own_time_reports_the_credential_store_as_unavailable()
    {
        var exception = Assert.ThrowsAsync<CredentialStoreUnavailableException>(() =>
            SessionRevocation.WithinDeadlineAsync(TimeSpan.FromMilliseconds(50), token => Task.Delay(Timeout.Infinite, token)));

        Assert.That(exception?.InnerException, Is.InstanceOf<OperationCanceledException>());
    }

    // Npgsql a veces envuelve la cancelación en otra excepción: también cuenta si la causa fue el plazo.
    [Test]
    public void A_wrapped_cancellation_caused_by_the_deadline_also_reports_the_store_as_unavailable()
    {
        Assert.ThrowsAsync<CredentialStoreUnavailableException>(() =>
            SessionRevocation.WithinDeadlineAsync(TimeSpan.FromMilliseconds(50), async token =>
            {
                try
                {
                    await Task.Delay(Timeout.Infinite, token);
                }
                catch (OperationCanceledException cancelled)
                {
                    throw new InvalidOperationException("The command was cancelled.", cancelled);
                }
            }));
    }

    [Test]
    public void A_cancellation_that_does_not_come_from_the_deadline_stays_a_cancellation()
    {
        using var aborted = new CancellationTokenSource();
        aborted.Cancel();

        var exception = Assert.CatchAsync<OperationCanceledException>(() =>
            SessionRevocation.WithinDeadlineAsync(TimeSpan.FromSeconds(5), _ => Task.Delay(Timeout.Infinite, aborted.Token)));

        Assert.That(exception?.CancellationToken, Is.EqualTo(aborted.Token));
    }

    [Test]
    public void Other_errors_pass_through_unchanged()
    {
        Assert.ThrowsAsync<InvalidOperationException>(() =>
            SessionRevocation.WithinDeadlineAsync(TimeSpan.FromSeconds(5), _ => throw new InvalidOperationException("boom")));
    }
}
