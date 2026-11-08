using MongoDB.Bson;
using MongoDB.Driver;
using Reviews.Application.Common.Interfaces;
using Reviews.Application.Common.Models;
using Reviews.Domain.Entities;

namespace Reviews.Infrastructure.Persistence;

// Read side: returns DTOs directly, Mongo does filtering, paging and projection
public class ReviewQueries(MongoDbContext context) : IReviewQueries
{
    private readonly IMongoCollection<Review> _reviews = context.Reviews;

    public async Task<ReviewDetailsDto?> GetByIdAsync(string id, CancellationToken ct)
    {
        if (!ObjectId.TryParse(id, out var objectId))
            return null;

        var r = await _reviews.Find(x => x.Id == objectId).FirstOrDefaultAsync(ct);
        if (r is null)
            return null;

        var comments = r.Comments
            .Select(c => new CommentDto(c.Id.ToString(), c.Author.UserId, c.Author.DisplayName, c.Text, c.CreatedAt))
            .ToList();

        return new ReviewDetailsDto(r.Id.ToString(), r.EventId, r.EventTitle, r.Author.UserId,
            r.Author.DisplayName, r.Rating.Value, r.Text, comments, r.CreatedAt, r.UpdatedAt, r.Version);
    }

    public async Task<PagedResult<ReviewDto>> GetListAsync(
        long? eventId, string? search, int page, int pageSize, CancellationToken ct)
    {
        var f = Builders<Review>.Filter;
        var filter = f.Empty;
        if (eventId is not null)
            filter &= f.Eq(r => r.EventId, eventId.Value);
        if (!string.IsNullOrWhiteSpace(search))
            filter &= f.Text(search); // uses the text index

        var total = await _reviews.CountDocumentsAsync(filter, cancellationToken: ct);

        // Aggregation pipeline: $match -> $sort -> $skip -> $limit -> $project
        // Comments are not loaded, only their count ($size)
        var projection = new BsonDocument
        {
            { "eventId", 1 },
            { "eventTitle", 1 },
            { "author", 1 },
            { "rating", 1 },
            { "text", 1 },
            { "createdAt", 1 },
            { "version", 1 },
            { "commentsCount", new BsonDocument("$size",
                new BsonDocument("$ifNull", new BsonArray { "$comments", new BsonArray() })) }
        };

        var docs = await _reviews.Aggregate()
            .Match(filter)
            .SortByDescending(r => r.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Limit(pageSize)
            .AppendStage<BsonDocument>(new BsonDocument("$project", projection))
            .ToListAsync(ct);

        var items = docs.Select(ToDto).ToList();
        return new PagedResult<ReviewDto>(items, page, pageSize, total);
    }

    private static ReviewDto ToDto(BsonDocument d) => new(
        d["_id"].AsObjectId.ToString(),
        d["eventId"].ToInt64(),
        d.GetValue("eventTitle", "").AsString,
        d["author"]["userId"].AsString,
        d["author"]["displayName"].AsString,
        d["rating"].ToInt32(),
        d["text"].AsString,
        d["commentsCount"].ToInt32(),
        d["createdAt"].ToUniversalTime(),
        d["version"].ToInt32());
}
