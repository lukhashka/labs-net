using FluentValidation;
using MediatR;
using Reviews.Application.Common.Interfaces;
using Reviews.Application.Common.Messaging;
using Reviews.Application.Common.Models;

namespace Reviews.Application.Reviews.Queries.GetReviewsList;

// Search uses the text index on "text"
public record GetReviewsListQuery(long? EventId, string? Search, int Page = 1, int PageSize = 20)
    : IQuery<PagedResult<ReviewDto>>;

public class GetReviewsListQueryHandler(IReviewQueries queries)
    : IRequestHandler<GetReviewsListQuery, PagedResult<ReviewDto>>
{
    public Task<PagedResult<ReviewDto>> Handle(GetReviewsListQuery q, CancellationToken ct)
        => queries.GetListAsync(q.EventId, q.Search, q.Page, Math.Clamp(q.PageSize, 1, 100), ct);
}

public class GetReviewsListQueryValidator : AbstractValidator<GetReviewsListQuery>
{
    public GetReviewsListQueryValidator()
    {
        RuleFor(x => x.Page).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 100);
        RuleFor(x => x.EventId).GreaterThan(0).When(x => x.EventId.HasValue);
        RuleFor(x => x.Search).MaximumLength(100);
    }
}
