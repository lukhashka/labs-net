using FluentValidation;
using Reviews.Domain.Interfaces;
using Reviews.Domain.ValueObjects;

namespace Reviews.Application.Reviews.Commands.CreateReview;

public class CreateReviewCommandValidator : AbstractValidator<CreateReviewCommand>
{
    public CreateReviewCommandValidator(IReviewRepository reviews)
    {
        RuleFor(x => x.EventId).GreaterThan(0);
        RuleFor(x => x.EventTitle).NotEmpty().MaximumLength(200);
        RuleFor(x => x.AuthorUserId).NotEmpty().MaximumLength(50);
        RuleFor(x => x.AuthorName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Rating).InclusiveBetween(Rating.Min, Rating.Max);
        RuleFor(x => x.Text).NotEmpty().MaximumLength(2000);

        // Async rule with a Mongo query (through the repository interface):
        // one author - one review per event
        RuleFor(x => x)
            .MustAsync(async (cmd, ct) => !await reviews.ExistsAsync(cmd.EventId, cmd.AuthorUserId, ct))
            .WithName("AuthorUserId")
            .WithMessage("You have already reviewed this event")
            .When(x => x.EventId > 0 && !string.IsNullOrWhiteSpace(x.AuthorUserId));
    }
}
