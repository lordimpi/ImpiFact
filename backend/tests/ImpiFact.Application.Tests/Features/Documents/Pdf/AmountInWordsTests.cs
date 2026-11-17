using ImpiFact.Application.Features.Documents.Pdf;

namespace ImpiFact.Application.Tests.Features.Documents.Pdf;

/// <summary>
/// El total en letras del PDF («SON: …»), como el <c>NumberSpellOut</c> del PHP: el entero y los centavos en español
/// (las reglas de ICU para <c>es-ES</c>), en mayúsculas.
/// </summary>
public sealed class AmountInWordsTests
{
    [TestCase(0, "CERO PESOS CON CERO CENTAVOS")]
    [TestCase(1, "UNO PESOS CON CERO CENTAVOS")]
    [TestCase(16, "DIECISÉIS PESOS CON CERO CENTAVOS")]
    [TestCase(21, "VEINTIUNO PESOS CON CERO CENTAVOS")]
    [TestCase(22, "VEINTIDÓS PESOS CON CERO CENTAVOS")]
    [TestCase(100, "CIEN PESOS CON CERO CENTAVOS")]
    [TestCase(101, "CIENTO UNO PESOS CON CERO CENTAVOS")]
    [TestCase(515, "QUINIENTOS QUINCE PESOS CON CERO CENTAVOS")]
    [TestCase(1000, "MIL PESOS CON CERO CENTAVOS")]
    [TestCase(1001, "MIL UNO PESOS CON CERO CENTAVOS")]
    [TestCase(21000, "VEINTIÚN MIL PESOS CON CERO CENTAVOS")]
    [TestCase(31000, "TREINTA Y UN MIL PESOS CON CERO CENTAVOS")]
    [TestCase(100000, "CIEN MIL PESOS CON CERO CENTAVOS")]
    [TestCase(201000, "DOSCIENTOS UN MIL PESOS CON CERO CENTAVOS")]
    [TestCase(1000000, "UN MILLÓN PESOS CON CERO CENTAVOS")]
    [TestCase(2000000, "DOS MILLONES PESOS CON CERO CENTAVOS")]
    [TestCase(21500000, "VEINTIÚN MILLONES QUINIENTOS MIL PESOS CON CERO CENTAVOS")]
    [TestCase(1000000000, "MIL MILLONES PESOS CON CERO CENTAVOS")]
    public void Spells_whole_pesos(long amount, string expected)
    {
        Assert.That(AmountInWords.Spell(amount, currencyName: null), Is.EqualTo(expected));
    }

    [Test]
    public void Spells_the_cents()
    {
        Assert.That(AmountInWords.Spell(1234567.89m, currencyName: null),
            Is.EqualTo("UN MILLÓN DOSCIENTOS TREINTA Y CUATRO MIL QUINIENTOS SESENTA Y SIETE PESOS CON OCHENTA Y NUEVE CENTAVOS"));
    }

    [Test]
    public void Uses_the_currency_name_when_the_document_has_another_currency()
    {
        Assert.That(AmountInWords.Spell(10.5m, "Dólar estadounidense"), Is.EqualTo("DIEZ DÓLAR ESTADOUNIDENSE CON CINCUENTA CENTAVOS"));
    }
}
