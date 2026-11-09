using FluentValidation;
using MediatR;
using Reviews.Application.Common.Interfaces;
using Reviews.Application.Common.Messaging;
using Reviews.Application.Common.Models;

namespace Reviews.Application.Reviews.Queries.GetRatingSummary;

// Average rating per event: one aggregation in Mongo instead of loading reviews into memory
public record GetRatingSummaryQuery(IReadOnlyCollection<long> EventIds) : IQuery<IReadOnlyList<EventRatingSummaryDto>>;

public class GetRatingSummaryQueryHandler(IReviewQueries queries)
    : IRequestHandler<GetRatingSummaryQuery, IReadOnlyList<EventRatingSummaryDto>>
{
    public Task<IReadOnlyList<EventRatingSummaryDto>> Handle(GetRatingSummaryQuery q, CancellationToken ct)
        => queries.GetRatingSummaryAsync(q.EventIds, ct);
}

public class GetRatingSummaryQueryValidator : AbstractValidator<GetRatingSummaryQuery>
{
    public GetRatingSummaryQueryValidator()
    {
        RuleFor(x => x.EventIds).NotEmpty();
        RuleForEach(x => x.EventIds).GreaterThan(0);
    }
}
