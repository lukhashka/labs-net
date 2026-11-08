namespace Catalog.Domain.Exceptions;

// Rule that needs other data from DB (e.g. unique code) is violated -> 409
public class ConflictException(string message) : Exception(message);
