using AutoMapper;
using Orders.Bll.Dtos;
using Orders.Domain.Models;

namespace Orders.Bll.Mapping;

public class OrdersProfile : Profile
{
    public OrdersProfile()
    {
        CreateMap<OrderItem, OrderItemDto>();
        CreateMap<OrderDetails, OrderDetailsDto>();
        CreateMap<Order, OrderDto>();
        CreateMap<OrderSummary, OrderSummaryDto>();
        CreateMap<TicketType, TicketTypeDto>();
        CreateMap<Customer, CustomerDto>()
            .ForCtorParam(nameof(CustomerDto.RowVersion), o => o.MapFrom(c => Convert.ToBase64String(c.RowVersion)));
    }
}
