using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Conventions;
using MongoDB.Bson.Serialization.Serializers;
using Reviews.Domain.Common;
using Reviews.Domain.Entities;
using Reviews.Domain.ValueObjects;

namespace Reviews.Infrastructure.Persistence;

// All BSON mapping lives here, so the Domain has no [Bson...] attributes
public static class MongoMappings
{
    private static readonly object Lock = new();
    private static bool _registered;

    public static void Register()
    {
        lock (Lock)
        {
            if (_registered) return; // class maps can be registered only once
            _registered = true;
        }

        // camelCase element names like in mongo-init.js from Lab 01
        ConventionRegistry.Register("reviewsConventions",
            new ConventionPack
            {
                new CamelCaseElementNameConvention(),
                new IgnoreExtraElementsConvention(true)
            },
            type => type.Namespace?.StartsWith("Reviews.Domain") == true);

        // Rating value object is stored as a plain number: "rating": 5
        BsonSerializer.RegisterSerializer(new RatingSerializer());

        BsonClassMap.RegisterClassMap<BaseEntity>(cm =>
        {
            cm.AutoMap();
            cm.MapIdMember(e => e.Id).SetSerializer(new ObjectIdSerializer());
            cm.MapMember(e => e.UpdatedAt).SetIgnoreIfNull(true);
            cm.SetIsRootClass(false);
        });

        // Value object: immutable, created through its constructor
        BsonClassMap.RegisterClassMap<Author>(cm =>
        {
            cm.MapProperty(a => a.UserId).SetElementName("userId");
            cm.MapProperty(a => a.DisplayName).SetElementName("displayName");
            cm.MapCreator(a => new Author(a.UserId, a.DisplayName));
        });

        BsonClassMap.RegisterClassMap<Comment>(cm => cm.AutoMap());

        BsonClassMap.RegisterClassMap<Review>(cm =>
        {
            cm.AutoMap();
            cm.UnmapMember(r => r.Comments);                     // public read-only view
            cm.MapField("_comments").SetElementName("comments"); // private field -> "comments"
            cm.MapMember(r => r.DiscussionId).SetIgnoreIfNull(true);
        });

        BsonClassMap.RegisterClassMap<Discussion>(cm => cm.AutoMap());
    }
}

// Custom serializer for the Rating value object (int <-> Rating)
public class RatingSerializer : SerializerBase<Rating>
{
    public override Rating Deserialize(BsonDeserializationContext context, BsonDeserializationArgs args)
        => new(context.Reader.ReadInt32());

    public override void Serialize(BsonSerializationContext context, BsonSerializationArgs args, Rating value)
        => context.Writer.WriteInt32(value.Value);
}
