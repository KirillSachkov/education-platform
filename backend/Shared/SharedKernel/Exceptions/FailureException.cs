#pragma warning disable RS0030 // SharedKernel exception types intentionally subclass System.Exception — this is the platform-canonical way to declare exception types.
﻿namespace SharedKernel.Exceptions;

public class FailureException : Exception
{
    public Error Error { get; } = null!;

    public FailureException(Error error)
        : base(error.GetMessage())
    {
        Error = error;
    }

    public FailureException()
    {
    }

    public FailureException(string message)
        : base(message)
    {
    }

    public FailureException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}