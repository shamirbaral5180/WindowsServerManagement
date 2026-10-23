namespace WindowsServerManagement.Core.Exceptions;

public abstract class AppException : Exception
{
    protected AppException(string message) : base(message)
    {
    }

    protected AppException(string message, Exception innerException) : base(message, innerException)
    {
    }
}

public sealed class ValidationException : AppException
{
    public ValidationException(string message) : base(message)
    {
    }

    public ValidationException(IEnumerable<string> errors)
        : base(string.Join(Environment.NewLine, errors))
    {
        Errors = errors.ToList();
    }

    public IReadOnlyList<string> Errors { get; } = [];
}

public sealed class NotFoundException : AppException
{
    public NotFoundException(string message) : base(message)
    {
    }
}

public sealed class ConflictException : AppException
{
    public ConflictException(string message) : base(message)
    {
    }
}

public sealed class ForbiddenException : AppException
{
    public ForbiddenException(string message) : base(message)
    {
    }
}

public sealed class NotConnectedException : AppException
{
    public NotConnectedException(string serverName)
        : base($"'{serverName}' is not connected. Connect to the server and try again.")
    {
    }
}

public sealed class RemoteOperationException : AppException
{
    public RemoteOperationException(string message) : base(message)
    {
    }

    public RemoteOperationException(string message, Exception innerException) : base(message, innerException)
    {
    }
}

public sealed class RemoteCapabilityException : AppException
{
    public RemoteCapabilityException(string message, IReadOnlyList<string> problems) : base(message)
    {
        Problems = problems;
    }

    public IReadOnlyList<string> Problems { get; }
}

public sealed class HostKeyVerificationException : AppException
{
    public HostKeyVerificationException(string host, string fingerprint, bool isMismatch)
        : base(isMismatch
            ? $"The SSH host key for '{host}' does not match the trusted fingerprint. The connection was rejected to prevent a man-in-the-middle attack. If the server was intentionally re-keyed, reset the trusted host key for this profile."
            : $"The SSH host key for '{host}' has not been trusted yet. Review the SHA-256 fingerprint before connecting.")
    {
        Host = host;
        Fingerprint = fingerprint;
        IsMismatch = isMismatch;
    }

    public string Host { get; }
    public string Fingerprint { get; }
    public bool IsMismatch { get; }
}
