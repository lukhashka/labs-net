using System.Data;
using AutoMapper;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Orders.Bll.Dtos;
using Orders.Bll.Mapping;
using Orders.Bll.Services;
using Orders.Dal;
using Orders.Dal.Repositories;
using Orders.Domain.Exceptions;
using Orders.Domain.Models;
using Xunit;

namespace Orders.Tests;

public class OrderServiceTests
{
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();
    private readonly IOrderRepository _orders = Substitute.For<IOrderRepository>();
    private readonly ITicketTypeRepository _ticketTypes = Substitute.For<ITicketTypeRepository>();
    private readonly ICustomerRepository _customers = Substitute.For<ICustomerRepository>();
    private readonly OrderService _service;

    public OrderServiceTests()
    {
        _uow.Orders.Returns(_orders);
        _uow.TicketTypes.Returns(_ticketTypes);
        _uow.Customers.Returns(_customers);
        var mapper = new MapperConfiguration(c => c.AddProfile<OrdersProfile>()).CreateMapper();
        _service = new OrderService(_uow, mapper, NullLogger<OrderService>.Instance);
    }

    [Fact]
    public void Mapping_configuration_is_valid()
        => new MapperConfiguration(c => c.AddProfile<OrdersProfile>()).AssertConfigurationIsValid();

    [Fact]
    public async Task Confirm_missing_order_throws_NotFound_and_rolls_back()
    {
        _orders.GetWithItemsAsync(999, Arg.Any<CancellationToken>()).Returns((Order?)null);

        await Assert.ThrowsAsync<NotFoundException>(() => _service.ConfirmOrderAsync(999));

        await _uow.Received(1).RollbackAsync(Arg.Any<CancellationToken>());
        await _uow.DidNotReceive().CommitAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Confirm_commits_when_stock_is_enough()
    {
        _orders.GetWithItemsAsync(1, Arg.Any<CancellationToken>()).Returns(OrderWithItems(1));

        await _service.ConfirmOrderAsync(1);

        await _uow.Received(1).BeginTransactionAsync(IsolationLevel.ReadCommitted, Arg.Any<CancellationToken>());
        await _ticketTypes.Received(1).DecreaseStockAsync(10, 2, Arg.Any<CancellationToken>());
        await _uow.Received(1).CommitAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Confirm_rolls_back_when_second_repository_fails()
    {
        _orders.GetWithItemsAsync(1, Arg.Any<CancellationToken>()).Returns(OrderWithItems(1));
        _ticketTypes.DecreaseStockAsync(10, 2, Arg.Any<CancellationToken>())
            .ThrowsAsync(new BusinessConflictException("Not enough tickets"));

        await Assert.ThrowsAsync<BusinessConflictException>(() => _service.ConfirmOrderAsync(1));

        await _orders.Received(1).ConfirmAsync(1, null, Arg.Any<CancellationToken>());   // the first step had been done
        await _uow.Received(1).RollbackAsync(Arg.Any<CancellationToken>());
        await _uow.DidNotReceive().CommitAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Create_stores_name_and_price_as_a_snapshot()
    {
        _customers.GetByIdAsync(1, Arg.Any<CancellationToken>()).Returns(new Customer { Id = 1 });
        _ticketTypes.GetByIdsAsync(Arg.Any<IEnumerable<long>>(), Arg.Any<CancellationToken>())
            .Returns([new TicketType { Id = 10, EventTitle = "Hack", Name = "VIP", Price = 100m, Stock = 5 }]);
        _orders.AddAsync(Arg.Any<Order>(), Arg.Any<CancellationToken>()).Returns(7L);
        _orders.GetWithItemsAsync(7, Arg.Any<CancellationToken>()).Returns(OrderWithItems(7));

        await _service.CreateAsync(new CreateOrderDto(1, "A", "a@b.c", null, [new CreateOrderItemDto(10, 2)]));

        await _orders.Received(1).AddItemAsync(Arg.Is<OrderItem>(i =>
            i.OrderId == 7 && i.EventTitle == "Hack" && i.TicketTypeName == "VIP" && i.UnitPrice == 100m && i.Units == 2),
            Arg.Any<CancellationToken>());
        await _orders.Received(1).AddAsync(Arg.Is<Order>(o => o.TotalAmount == 200m), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Create_more_than_stock_is_a_conflict()
    {
        _customers.GetByIdAsync(1, Arg.Any<CancellationToken>()).Returns(new Customer { Id = 1 });
        _ticketTypes.GetByIdsAsync(Arg.Any<IEnumerable<long>>(), Arg.Any<CancellationToken>())
            .Returns([new TicketType { Id = 10, Stock = 1 }]);

        await Assert.ThrowsAsync<BusinessConflictException>(() =>
            _service.CreateAsync(new CreateOrderDto(1, "A", "a@b.c", null, [new CreateOrderItemDto(10, 2)])));

        await _uow.Received(1).RollbackAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Create_with_duplicated_ticket_type_is_invalid()
        => await Assert.ThrowsAsync<ValidationException>(() => _service.CreateAsync(new CreateOrderDto(
            1, "A", "a@b.c", null, [new CreateOrderItemDto(10, 1), new CreateOrderItemDto(10, 2)])));

    private static Order OrderWithItems(long id) => new()
    {
        Id = id,
        Items = [new OrderItem { OrderId = id, TicketTypeId = 10, Units = 2, UnitPrice = 100m, EventTitle = "Hack", TicketTypeName = "VIP" }]
    };
}
