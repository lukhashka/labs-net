using MongoDB.Bson;

namespace Reviews.Domain.Common;

public abstract class BaseEntity
{
    public ObjectId Id { get; protected set; } = ObjectId.GenerateNewId();
    public DateTime CreatedAt { get; protected set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; protected set; }
    public int Version { get; protected set; } = 1;

    protected void Touch() => UpdatedAt = DateTime.UtcNow;

    // Called by the repository right before saving (optimistic concurrency)
    public void BumpVersion() => Version++;
}
