namespace Orders.Domain.Exceptions;

// Declared in Domain so that both DAL and BLL can throw them. Global handler in the API maps them to HTTP statuses.
public class NotFoundException(string message) : Exception(message);            // 404

public class BusinessConflictException(string message) : Exception(message);    // 409

public class ValidationException(string message) : Exception(message);          // 400
