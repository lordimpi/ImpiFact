using ImpiFact.Application.Abstractions.Documents;
using ImpiFact.Application.Abstractions.Handlers;
using ImpiFact.Application.Features.Companies.UpsertResolution;
using ImpiFact.Domain.Companies;
using ImpiFact.Domain.Documents;
using Microsoft.EntityFrameworkCore;

namespace ImpiFact.Integration.Tests.Documents;

/// <summary>
/// Crear una resolución con el caso de uso real (con sus decoradores y su transacción) deja listo su consecutivo
/// <c>R:{resolution_id}</c> en el <c>from</c> del rango, y actualizarla nunca lo hace bajar.
/// </summary>
[TestFixture]
[Category("Integration")]
public sealed class ResolutionSequenceTests
{
    [Test]
    public async Task Creating_a_resolution_creates_its_numbering_sequence_at_the_start_of_the_range()
    {
        var companyId = await DocumentsDatabase.CreateCompanyAsync();

        await UpsertAsync(companyId, from: 990000000, to: 995000000);

        var (resolution, sequence) = await ReadAsync(companyId);
        Assert.Multiple(() =>
        {
            Assert.That(sequence.ScopeKey, Is.EqualTo(NumberingScope.ForResolution(resolution.Id)));
            Assert.That(sequence.NextNumber, Is.EqualTo(990000000));
        });
    }

    [Test]
    public async Task The_first_number_taken_after_creating_a_resolution_is_the_start_of_its_range()
    {
        var companyId = await DocumentsDatabase.CreateCompanyAsync();
        await UpsertAsync(companyId, from: 990000000, to: 995000000);
        var (resolution, _) = await ReadAsync(companyId);

        await using var session = DocumentsDatabase.Open(companyId);
        await using var transaction = await session.UnitOfWork.BeginTransactionAsync(CancellationToken.None);
        var number = await session.Get<INumberingSequences>()
            .TakeNextAsync(companyId, NumberingScope.ForResolution(resolution.Id), CancellationToken.None);
        await transaction.CommitAsync(CancellationToken.None);

        Assert.That(number, Is.EqualTo(990000000));
    }

    [Test]
    public async Task Updating_a_resolution_to_a_higher_range_moves_its_sequence_forward()
    {
        var companyId = await DocumentsDatabase.CreateCompanyAsync();
        await UpsertAsync(companyId, from: 1, to: 100);

        await UpsertAsync(companyId, from: 500, to: 1000);

        var (_, sequence) = await ReadAsync(companyId);
        Assert.That(sequence.NextNumber, Is.EqualTo(500));
    }

    [Test]
    public async Task Updating_a_resolution_to_a_lower_range_never_moves_its_sequence_back()
    {
        var companyId = await DocumentsDatabase.CreateCompanyAsync();
        await UpsertAsync(companyId, from: 500, to: 1000);

        await UpsertAsync(companyId, from: 1, to: 1000);

        var (_, sequence) = await ReadAsync(companyId);
        Assert.That(sequence.NextNumber, Is.EqualTo(500));
    }

    private static async Task UpsertAsync(Guid companyId, long from, long to)
    {
        await using var session = DocumentsDatabase.Open(companyId);
        var handler = session.Get<IHandler<UpsertResolutionCommand, ResolutionSavedResponse>>();
        var result = await handler.HandleAsync(
            new UpsertResolutionCommand { TypeDocumentId = 4, Prefix = "NC", From = from, To = to }, CancellationToken.None);
        Assert.That(result.IsSuccess, Is.True, () => result.Error.Message);
    }

    private static async Task<(Resolution Resolution, NumberingSequence Sequence)> ReadAsync(Guid companyId)
    {
        await using var session = DocumentsDatabase.Open(companyId);
        var resolution = await session.Db.Set<Resolution>().SingleAsync();
        var sequences = await session.Db.Set<NumberingSequence>().ToListAsync();
        Assert.That(sequences, Has.Count.EqualTo(1), "One sequence per resolution.");
        return (resolution, sequences[0]);
    }
}
