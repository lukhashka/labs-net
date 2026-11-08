using MediatR;
using Reviews.Domain.Entities;
using Reviews.Domain.Interfaces;
using Reviews.Domain.ValueObjects;

namespace Reviews.Application.Reviews.Commands.CreateReview;

public class CreateReviewCommandHandler(IReviewRepository reviews)
    : IRequestHandler<CreateReviewCommand, string>
{
    public async Task<string> Handle(CreateReviewCommand cmd, CancellationToken ct)
    {
        var review = new Review(
            cmd.EventId,
            cmd.EventTitle,
            new Author(cmd.AuthorUserId, cmd.AuthorName),
            new Rating(cmd.Rating),
            cmd.Text);

        await reviews.AddAsync(review, ct);
        return review.Id.ToString();
    }
}
