#pragma warning disable RS0030 // SharedKernel exception types intentionally subclass System.Exception — this is the platform-canonical way to declare exception types.
﻿namespace SharedKernel.Exceptions;

public class ConflictException : Exception
{
    public Error Error { get; } = null!;

    public ConflictException(Error error)
        : base(error.GetMessage())
    {
        Error = error;
    }

    public ConflictException()
    {
    }

    public ConflictException(string message)
        : base(message)
    {
    }

    public ConflictException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}