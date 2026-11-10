using FluentValidation;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using MediatR;
using Reviews.Application.Reviews.Queries.GetRatingSummary;
using Reviews.Application.Reviews.Queries.GetReviewsList;
using Reviews.Grpc;

namespace Reviews.WebApi.GrpcServices;

// Adapter like the controller: only sends queries to MediatR and converts the result
[Microsoft.AspNetCore.Authorization.Authorize(Policy = "reviews.read")]   // Lab 09: Client Credentials of the Aggregator
public class ReviewsGrpcService(ISender mediator) : ReviewsService.ReviewsServiceBase
{
    public override async Task<RatingSummaryReply> GetRatingSummary(GetRatingSummaryRequest request, ServerCallContext context)
    {
        try
        {
            var ratings = await mediator.Send(new GetRatingSummaryQuery(request.EventIds.Distinct().ToList()), context.CancellationToken);
            return new RatingSummaryReply
            {
                Ratings = { ratings.Select(r => new EventRating { EventId = r.EventId, Average = r.Average, Count = r.Count }) }
            };
        }
        catch (ValidationException ex)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, ex.Message));
        }
    }

    public override async Task<LatestReviewsReply> GetLatestReviews(GetLatestReviewsRequest request, ServerCallContext context)
    {
        try
        {
            var page = await mediator.Send(new GetReviewsListQuery(request.EventId, null, 1, request.Limit), context.CancellationToken);
            return new LatestReviewsReply
            {
                Reviews =
                {
                    page.Items.Select(r => new ReviewReply
                    {
                        AuthorName = r.AuthorName,
                        Rating = r.Rating,
                        Text = r.Text,
                        CreatedAt = Timestamp.FromDateTime(DateTime.SpecifyKind(r.CreatedAt, DateTimeKind.Utc))
                    })
                }
            };
        }
        catch (ValidationException ex)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, ex.Message));
        }
    }
}
