using AutoMapper;
using FluentValidation;
using MediatR;
using MongoDB.Bson;
using Reviews.Application.Common.Messaging;
using Reviews.Application.Common.Models;
using Reviews.Domain.Entities;
using Reviews.Domain.Exceptions;
using Reviews.Domain.Interfaces;

namespace Reviews.Application.Discussions.Queries.GetDiscussionById;

public record GetDiscussionByIdQuery(string Id) : IQuery<DiscussionDto>;

public class GetDiscussionByIdQueryHandler(IDiscussionRepository discussions, IMapper mapper)
    : IRequestHandler<GetDiscussionByIdQuery, DiscussionDto>
{
    public async Task<DiscussionDto> Handle(GetDiscussionByIdQuery q, CancellationToken ct)
    {
        var d = await discussions.GetByIdAsync(ObjectId.Parse(q.Id), ct)
                ?? throw new NotFoundException(nameof(Discussion), q.Id);

        return mapper.Map<DiscussionDto>(d);   // ObjectId -> string, Author VO -> name
    }
}

public class GetDiscussionByIdQueryValidator : AbstractValidator<GetDiscussionByIdQuery>
{
    public GetDiscussionByIdQueryValidator()
        => RuleFor(x => x.Id).Must(id => ObjectId.TryParse(id, out _))
            .WithMessage("Id must be a valid ObjectId");
}
