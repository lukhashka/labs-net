using Catalog.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Catalog.Dal.Seeding;

// Programmatic seed after MigrateAsync. Venues and Categories come from HasData (fixed Ids),
// here — dependent data. Idempotent: every block checks existence before insert.
public static class DbInitializer
{
    public static async Task SeedAsync(CatalogDbContext db, ILogger logger, CancellationToken ct = default)
    {
        if (await db.Events.IgnoreQueryFilters().AnyAsync(ct))
        {
            logger.LogInformation("Catalog seed skipped: {Count} events already exist",
                await db.Events.IgnoreQueryFilters().CountAsync(ct));
            return;
        }

        var concert = new Event("Осінній концерт ЧНУ", "osinnii-kontsert-2026",
            new DateTime(2026, 11, 14, 18, 0, 0, DateTimeKind.Utc), new DateTime(2026, 11, 14, 21, 0, 0, DateTimeKind.Utc), 1);
        concert.SetDetail("Студентський концерт до дня університету", "Студрада ЧНУ", 0);
        concert.AddCategory(1);
        concert.AddCategory(5);

        var hackathon = new Event("CampusHack 2026", "campushack-2026",
            new DateTime(2026, 11, 21, 9, 0, 0, DateTimeKind.Utc), new DateTime(2026, 11, 22, 18, 0, 0, DateTimeKind.Utc), 3);
        hackathon.SetDetail("24-годинний хакатон для студентів ФМІ", "Кафедра КН", 16);
        hackathon.AddCategory(2);
        hackathon.AddCategory(6);

        var lecture = new Event("Відкрита лекція: EF Core на практиці", "lecture-ef-core",
            new DateTime(2026, 11, 5, 14, 0, 0, DateTimeKind.Utc), new DateTime(2026, 11, 5, 15, 30, 0, DateTimeKind.Utc), 1);
        lecture.SetDetail("Code First, міграції, завантаження пов'язаних даних", "Кафедра КН", 0);
        lecture.AddCategory(3);

        var match = new Event("Кубок факультетів з волейболу", "volleyball-cup-2026",
            new DateTime(2026, 12, 3, 16, 0, 0, DateTimeKind.Utc), new DateTime(2026, 12, 3, 20, 0, 0, DateTimeKind.Utc), 2);
        match.AddCategory(4);

        db.Events.AddRange(concert, hackathon, lecture, match);
        await db.SaveChangesAsync(ct);                                   // Ids are generated here

        db.TicketTypes.AddRange(
            new TicketType(concert.Id, "Standard", "CONC26-STD", 150m, 300),
            new TicketType(concert.Id, "VIP", "CONC26-VIP", 400m, 50),
            new TicketType(concert.Id, "Student", "CONC26-STU", 80m, 50),
            new TicketType(hackathon.Id, "Participant", "HACK26-PRT", 50m, 60),
            new TicketType(hackathon.Id, "Guest", "HACK26-GST", 20m, 20),
            new TicketType(lecture.Id, "Free", "LECT-EF-FREE", 1m, 120),
            new TicketType(match.Id, "Standard", "VOLL26-STD", 30m, 800),
            new TicketType(match.Id, "Student", "VOLL26-STU", 15m, 400));
        await db.SaveChangesAsync(ct);

        logger.LogInformation("Catalog seed completed: {Events} events, {TicketTypes} ticket types",
            await db.Events.CountAsync(ct), await db.TicketTypes.CountAsync(ct));
    }
}
