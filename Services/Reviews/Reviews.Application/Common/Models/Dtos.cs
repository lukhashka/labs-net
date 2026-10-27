namespace Reviews.Application.Common.Models;

// Read models returned by queries (no domain objects leak to the API)
public record ReviewDto(
    string Id,
    long EventId,
    string EventTitle,
    string AuthorUserId,
    string AuthorName,
    int Rating,
    string Text,
    int CommentsCount,
    DateTime CreatedAt,
    int Version);

public record CommentDto(string Id, string AuthorUserId, string AuthorName, string Text, DateTime CreatedAt);

public record ReviewDetailsDto(
    string Id,
    long EventId,
    string EventTitle,
    string AuthorUserId,
    string AuthorName,
    int Rating,
    string Text,
    IReadOnlyList<CommentDto> Comments,
    DateTime CreatedAt,
    DateTime? UpdatedAt,
    int Version);

public record DiscussionDto(
    string Id,
    long EventId,
    string EventTitle,
    string Title,
    string StartedBy,
    bool IsClosed,
    DateTime CreatedAt,
    int Version);
