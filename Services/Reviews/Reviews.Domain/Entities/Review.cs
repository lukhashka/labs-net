using MongoDB.Bson;
using Reviews.Domain.Common;
using Reviews.Domain.Exceptions;
using Reviews.Domain.ValueObjects;

namespace Reviews.Domain.Entities;

// Aggregate root: review of a CampusTix event
public class Review : BaseEntity
{
    // not readonly: the Mongo driver sets this field via reflection (mapped as "comments")
    private List<Comment> _comments = new();

    public long EventId { get; private set; }               // reference to Catalog.Event
    public string EventTitle { get; private set; } = "";    // denormalized copy
    public Author Author { get; private set; } = null!;
    public Rating Rating { get; private set; } = null!;
    public string Text { get; private set; } = "";
    public ObjectId? DiscussionId { get; private set; }

    public IReadOnlyCollection<Comment> Comments => _comments.AsReadOnly();

    private Review() { } // for the Mongo serializer

    public Review(long eventId, string eventTitle, Author author, Rating rating, string text)
    {
        if (eventId <= 0)
            throw new DomainException("EventId must be positive");
        if (string.IsNullOrWhiteSpace(text))
            throw new DomainException("Review text is required");

        EventId = eventId;
        EventTitle = eventTitle;
        Author = author;
        Rating = rating;
        Text = text;
    }

    public void Edit(Rating rating, string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            throw new DomainException("Review text is required");
        Rating = rating;
        Text = text;
        Touch();
    }

    public void AddComment(Author author, string text)
    {
        _comments.Add(new Comment(author, text));
        Touch();
    }

    // Will be called when Catalog publishes event.renamed (Lab 08)
    public void RenameEvent(string newTitle)
    {
        EventTitle = newTitle;
        Touch();
    }

    public void AttachToDiscussion(ObjectId discussionId)
    {
        DiscussionId = discussionId;
        Touch();
    }
}
