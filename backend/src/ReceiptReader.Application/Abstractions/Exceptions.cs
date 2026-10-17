namespace ReceiptReader.Application.Abstractions;

public sealed class NotFoundException(string message) : Exception(message);

/// <summary>The request is valid but conflicts with the current state (e.g. editing an approved receipt).</summary>
public sealed class ConflictException(string message) : Exception(message);

public sealed class BadRequestException(string message) : Exception(message);

/// <summary>The signed-in user is not allowed to do this.</summary>
public sealed class ForbiddenException(string message) : Exception(message);

/// <summary>A service the request needs (the AI) didn't answer usefully; trying again later may work.</summary>
public sealed class UnavailableException(string message) : Exception(message);
