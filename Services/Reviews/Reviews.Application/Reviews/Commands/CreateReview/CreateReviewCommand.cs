using Reviews.Application.Common.Messaging;

namespace Reviews.Application.Reviews.Commands.CreateReview;

// Returns the id (ObjectId as string) of the new review
public record CreateReviewCommand(
    long EventId,
    string EventTitle,
    string AuthorUserId,
    string AuthorName,
    int Rating,
    string Text) : ICommand<string>;
