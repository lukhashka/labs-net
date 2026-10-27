namespace Catalog.Domain.Exceptions;

// Requested resource does not exist -> 404
public class EntityNotFoundException(string entity, object id)
    : Exception($"{entity} with id '{id}' was not found");
