#pragma warning disable RS0030 // SharedKernel exception types intentionally subclass System.Exception — this is the platform-canonical way to declare exception types.
﻿namespace SharedKernel.Exceptions;

public class ValidationException : Exception
{
    public Error Error { get; } = null!;

    public ValidationException(Error error)
        : base(error.GetMessage())
    {
        Error = error;
    }

    public ValidationException()
    {
    }

    public ValidationException(string message)
        : base(message)
    {
    }

    public ValidationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}