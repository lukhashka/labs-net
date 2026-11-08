using MediatR;

namespace Reviews.Application.Common.Messaging;

// Marker interfaces: commands change state, queries only read it (CQRS)
public interface ICommand : IRequest { }

public interface ICommand<TResponse> : IRequest<TResponse> { }

public interface IQuery<TResponse> : IRequest<TResponse> { }
