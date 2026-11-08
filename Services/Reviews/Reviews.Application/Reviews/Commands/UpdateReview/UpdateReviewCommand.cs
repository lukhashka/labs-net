using Reviews.Application.Common.Messaging;

namespace Reviews.Application.Reviews.Commands.UpdateReview;

// Version = the version the client has read (optimistic concurrency)
public record UpdateReviewCommand(string Id, int Rating, string Text, int Version) : ICommand;
