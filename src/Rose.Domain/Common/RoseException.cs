namespace Rose.Domain.Common;

/// <summary>Base for errors the user caused and can fix. The API maps each kind to an HTTP status.</summary>
public abstract class RoseException(string message) : Exception(message);

/// <summary>Bad input (HTTP 400).</summary>
public sealed class ValidationException(string message) : RoseException(message);

/// <summary>Something with the same name already exists (HTTP 409).</summary>
public sealed class ConflictException(string message) : RoseException(message);

/// <summary>The thing asked for does not exist (HTTP 404).</summary>
public sealed class NotFoundException(string message) : RoseException(message);