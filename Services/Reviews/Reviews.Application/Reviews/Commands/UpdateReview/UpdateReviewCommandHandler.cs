using MediatR;
using MongoDB.Bson;
using Reviews.Domain.Entities;
using Reviews.Domain.Exceptions;
using Reviews.Domain.Interfaces;
using Reviews.Domain.ValueObjects;

namespace Reviews.Application.Reviews.Commands.UpdateReview;

public class UpdateReviewCommandHandler(IReviewRepository reviews) : IRequestHandler<UpdateReviewCommand>
{
    public async Task Handle(UpdateReviewCommand cmd, CancellationToken ct)
    {
        var review = await reviews.GetByIdAsync(ObjectId.Parse(cmd.Id), ct)
                     ?? throw new NotFoundException(nameof(Review), cmd.Id);

        // Client edited an old copy of the document
        if (review.Version != cmd.Version)
            throw new ConflictException(
                $"Review {cmd.Id} has version {review.Version}, but you sent {cmd.Version}");

        review.Edit(new Rating(cmd.Rating), cmd.Text);
        await reviews.UpdateAsync(review, ct); // second check of Version inside the filter
    }
}
