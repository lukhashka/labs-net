namespace Catalog.Domain.Exceptions;

// Business rule of an entity is violated -> 409
public class DomainException(string message) : Exception(message);
