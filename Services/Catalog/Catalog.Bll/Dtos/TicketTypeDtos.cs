using System.ComponentModel.DataAnnotations;

namespace Catalog.Bll.Dtos;

public record TicketTypeDto(int Id, int EventId, string Name, string Code, decimal Price, int Capacity, int AvailableStock);

public record CreateTicketTypeDto(
    [Range(1, int.MaxValue)] int EventId,
    [Required, StringLength(50)] string Name,
    [Required, StringLength(50)] string Code,
    decimal Price,
    int Capacity);

public record ChangePriceDto(decimal Price);

public record StockChangeDto([Range(1, 10000)] int Quantity);
