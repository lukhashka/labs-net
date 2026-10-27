using Microsoft.Extensions.Logging;
using MongoDB.Driver;
using Reviews.Domain.Entities;
using Reviews.Domain.ValueObjects;
using Reviews.Infrastructure.Persistence;

namespace Reviews.Infrastructure.Seeding;

// Idempotent: inserts test data only into empty collections
public class ReviewSeeder(MongoDbContext context, ILogger<ReviewSeeder> logger) : IDataSeeder
{
    public async Task SeedAsync(CancellationToken ct = default)
    {
        if (await context.Reviews.EstimatedDocumentCountAsync(cancellationToken: ct) > 0)
        {
            logger.LogInformation("Reviews already seeded, skipping");
            return;
        }

        var hackathonTalk = new Discussion(12, "Хакатон ЧНУ 2026", "Хто шукає команду?",
            new Author("u-17", "Олена"));
        var concertTalk = new Discussion(15, "Осінній концерт", "Чи буде запис виступу?",
            new Author("u-30", "Ігор"));

        var review1 = new Review(12, "Хакатон ЧНУ 2026", new Author("u-17", "Олена"),
            new Rating(5), "Найкращий хакатон за 3 роки");
        review1.AddComment(new Author("u-21", "Андрій"), "Згоден!");
        review1.AttachToDiscussion(hackathonTalk.Id);

        var review2 = new Review(12, "Хакатон ЧНУ 2026", new Author("u-21", "Андрій"),
            new Rating(4), "Цікаві задачі, але мало кави");

        var review3 = new Review(15, "Осінній концерт", new Author("u-17", "Олена"),
            new Rating(5), "Чудовий звук і атмосфера");

        var review4 = new Review(15, "Осінній концерт", new Author("u-30", "Ігор"),
            new Rating(3), "Довго чекали на вході");
        review4.AttachToDiscussion(concertTalk.Id);

        // Bulk insert in one round trip
        await context.Discussions.InsertManyAsync(new[] { hackathonTalk, concertTalk }, cancellationToken: ct);
        await context.Reviews.InsertManyAsync(new[] { review1, review2, review3, review4 }, cancellationToken: ct);

        logger.LogInformation("Seeded 4 reviews and 2 discussions");
    }
}
