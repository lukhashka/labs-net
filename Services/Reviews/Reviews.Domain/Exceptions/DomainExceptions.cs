namespace Reviews.Domain.Exceptions;

// Business rule violation inside the domain model -> 400
public class DomainException(string message) : Exception(message);

// Entity does not exist -> 404
public class NotFoundException(string entity, object key)
    : Exception($"{entity} with id '{key}' was not found");

// Document was changed by someone else (Version mismatch) -> 409
public class ConflictException(string message) : Exception(message);
