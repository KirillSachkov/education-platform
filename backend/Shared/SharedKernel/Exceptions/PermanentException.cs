#pragma warning disable RS0030 // SharedKernel exception types intentionally subclass System.Exception — this is the platform-canonical way to declare exception types.
﻿namespace SharedKernel.Exceptions;

public class PermanentException : Exception
{
    public Error Error { get; } = null!;

    public PermanentException(Error error)
        : base(error.GetMessage())
    {
        Error = error;
    }

    public PermanentException()
    {
    }

    public PermanentException(string message)
        : base(message)
    {
    }

    public PermanentException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}