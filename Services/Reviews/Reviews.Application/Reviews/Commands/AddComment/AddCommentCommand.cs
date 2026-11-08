using FluentValidation;
using MediatR;
using MongoDB.Bson;
using Reviews.Application.Common.Messaging;
using Reviews.Domain.Entities;
using Reviews.Domain.Exceptions;
using Reviews.Domain.Interfaces;
using Reviews.Domain.ValueObjects;

namespace Reviews.Application.Reviews.Commands.AddComment;

public record AddCommentCommand(string ReviewId, string AuthorUserId, string AuthorName, string Text)
    : ICommand<string>;

public class AddCommentCommandHandler(IReviewRepository reviews) : IRequestHandler<AddCommentCommand, string>
{
    public async Task<string> Handle(AddCommentCommand cmd, CancellationToken ct)
    {
        var review = await reviews.GetByIdAsync(ObjectId.Parse(cmd.ReviewId), ct)
                     ?? throw new NotFoundException(nameof(Review), cmd.ReviewId);

        review.AddComment(new Author(cmd.AuthorUserId, cmd.AuthorName), cmd.Text);
        await reviews.UpdateAsync(review, ct); // Version check protects from lost updates

        return review.Comments.Last().Id.ToString();
    }
}

public class AddCommentCommandValidator : AbstractValidator<AddCommentCommand>
{
    public AddCommentCommandValidator()
    {
        RuleFor(x => x.ReviewId).Must(id => ObjectId.TryParse(id, out _))
            .WithMessage("ReviewId must be a valid ObjectId");
        RuleFor(x => x.AuthorUserId).NotEmpty().MaximumLength(50);
        RuleFor(x => x.AuthorName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Text).NotEmpty().MaximumLength(1000);
    }
}
