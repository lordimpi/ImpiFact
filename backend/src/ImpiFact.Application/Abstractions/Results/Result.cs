namespace ImpiFact.Application.Abstractions.Results;

/// <summary>
/// Resultado de un caso de uso: un valor si salió bien, o un <see cref="Results.Error"/> si no.
/// Leer el valor de un resultado fallido, o el error de uno exitoso, es un error de programación y lanza
/// una excepción, para que nunca se use un valor que no existe.
/// </summary>
public sealed class Result<T>
{
    private readonly T? _value;
    private readonly Error _error;

    private Result(T value)
    {
        IsSuccess = true;
        _value = value;
    }

    private Result(Error error)
    {
        _error = error;
    }

    public bool IsSuccess { get; }

    public T Value => IsSuccess
        ? _value!
        : throw new InvalidOperationException($"Cannot read the value of a failed result ({_error.Code}).");

    public Error Error => IsSuccess
        ? throw new InvalidOperationException("Cannot read the error of a successful result.")
        : _error;

    public static Result<T> Ok(T value) => new(value);

    public static Result<T> Fail(Error error) => new(error);

    public static implicit operator Result<T>(T value) => Ok(value);

    public static implicit operator Result<T>(Error error) => Fail(error);
}
