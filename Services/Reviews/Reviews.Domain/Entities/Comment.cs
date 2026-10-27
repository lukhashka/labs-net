using Reviews.Domain.Common;
using Reviews.Domain.Exceptions;
using Reviews.Domain.ValueObjects;

namespace Reviews.Domain.Entities;

// Embedded into Review.comments[]
public class Comment : BaseEntity
{
    public Author Author { get; private set; } = null!;
    public string Text { get; private set; } = "";

    private Comment() { } // for the Mongo serializer

    public Comment(Author author, string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            throw new DomainException("Comment text is required");
        Author = author;
        Text = text;
    }
}
