using ImpiFact.Application.Abstractions.Results;

namespace ImpiFact.Application.Features.Billing;

/// <summary>Errores esperados del módulo Billing (planes y consumo). Los textos que vienen del PHP se conservan tal cual.</summary>
public static class BillingErrors
{
    /// <summary>Texto del PHP (<c>InvoiceController::store</c>, <c>type_plan->state == false</c>).</summary>
    public static readonly Error PlanInactive = new(
        "plan.inactive",
        "El plan en el que esta registrado la empresa se encuentra en el momento INACTIVO para enviar documentos electronicos...",
        ErrorType.Forbidden);

    /// <summary>Texto del PHP para el plan mensual (periodo 1) vencido o agotado.</summary>
    public static readonly Error MonthlyLimitReached = new(
        "plan.limit_reached",
        "La empresa ha llegado al limite de tiempo/documentos del plan por mensualidad, por favor renueve su membresia...",
        ErrorType.Forbidden);

    /// <summary>Texto del PHP para el plan anual (periodo 2) vencido o agotado.</summary>
    public static readonly Error YearlyLimitReached = new(
        "plan.limit_reached",
        "La empresa ha llegado al limite de tiempo/documentos del plan por anualidad, por favor renueve su membresia...",
        ErrorType.Forbidden);

    /// <summary>Texto del PHP para el paquete (periodo 3) agotado.</summary>
    public static readonly Error PackageLimitReached = new(
        "plan.limit_reached",
        "La empresa ha llegado al limite de documentos del plan por paquetes, por favor renueve su membresia...",
        ErrorType.Forbidden);

    /// <summary>Administrar planes es una operación de plataforma (credencial sin empresa con alcance <c>platform:*</c>).</summary>
    public static readonly Error PlatformOnly = new(
        "plan.platform_only",
        "Solo la plataforma puede administrar los planes.",
        ErrorType.Forbidden);

    /// <summary>
    /// El plan no existe. El PHP respondía un 500 con el texto de la excepción de Laravel (<c>firstOrFail</c>); aquí es
    /// un 404.
    /// </summary>
    public static Error PlanNotFound(int planId) => new(
        "plan.not_found",
        $"No existe el plan con ID {planId}.",
        ErrorType.NotFound);

    public static readonly Error CompanyNotFound = new(
        "company.not_found",
        "No existe una empresa con ese NIT.",
        ErrorType.NotFound);
}
