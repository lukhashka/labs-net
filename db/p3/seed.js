// CampusTix · ReviewsDb. Seed data and the computed eventRatings collection.
// Idempotent. Run after collections.js:  mongosh "mongodb://localhost:27017/ReviewsDb" seed.js
// eventId values match CatalogDb (logical references, no constraints between databases).

const database = db.getSiblingDB("ReviewsDb");

// Fixed _id values make the seed repeatable: upsert with $setOnInsert never creates a duplicate.
function seed(collection, documents) {
  documents.forEach((doc) => {
    const { _id, ...rest } = doc;
    database[collection].updateOne({ _id }, { $setOnInsert: rest }, { upsert: true });
  });
}

const hackTalk = ObjectId("66f0c3000000000000000001");
const concertTalk = ObjectId("66f0c3000000000000000002");
const olena = { userId: "u-17", displayName: "Олена" };
const andrii = { userId: "u-21", displayName: "Андрій" };
const ihor = { userId: "u-30", displayName: "Ігор" };

seed("discussions", [
  {
    _id: hackTalk, eventId: NumberLong("2"), eventTitle: "CampusHack 2026", title: "Хто шукає команду?",
    startedBy: olena, isClosed: false, createdAt: ISODate("2026-10-10T10:00:00Z"), version: NumberInt(1),
    // hybrid: the latest messages are embedded, the whole history lives in "messages"
    lastMessages: [
      { author: andrii, text: "Шукаю бекендера", createdAt: ISODate("2026-10-10T11:00:00Z") },
      { author: ihor, text: "Я з вами", createdAt: ISODate("2026-10-10T11:05:00Z") },
    ],
  },
  {
    _id: concertTalk, eventId: NumberLong("1"), eventTitle: "Осінній концерт ЧНУ", title: "Чи буде запис виступу?",
    startedBy: ihor, isClosed: true, createdAt: ISODate("2026-10-12T09:00:00Z"), version: NumberInt(1),
  },
]);

seed("messages", [
  { _id: ObjectId("66f0c4000000000000000001"), discussionId: hackTalk, author: olena, text: "Хто шукає команду?", createdAt: ISODate("2026-10-10T10:00:00Z") },
  { _id: ObjectId("66f0c4000000000000000002"), discussionId: hackTalk, author: andrii, text: "Шукаю бекендера", createdAt: ISODate("2026-10-10T11:00:00Z") },
  { _id: ObjectId("66f0c4000000000000000003"), discussionId: hackTalk, author: ihor, text: "Я з вами", createdAt: ISODate("2026-10-10T11:05:00Z") },
]);

seed("reviews", [
  {
    _id: ObjectId("66f0c2000000000000000001"), eventId: NumberLong("2"), eventTitle: "CampusHack 2026", kind: "review",
    author: olena, rating: NumberInt(5), text: "Найкращий хакатон за 3 роки", photos: ["hack-1.jpg", "hack-2.jpg"],
    comments: [{ _id: ObjectId("66f0c5000000000000000001"), author: andrii, text: "Згоден!", createdAt: ISODate("2026-10-11T18:00:00Z") }],
    discussionId: hackTalk, createdAt: ISODate("2026-10-11T17:00:00Z"), version: NumberInt(1),
  },
  {
    _id: ObjectId("66f0c2000000000000000002"), eventId: NumberLong("2"), eventTitle: "CampusHack 2026", kind: "review",
    author: andrii, rating: NumberInt(4), text: "Цікаві задачі, але мало кави", comments: [],
    createdAt: ISODate("2026-10-11T19:00:00Z"), version: NumberInt(1),
  },
  {
    // a different set of fields in the same collection: verified attendee has ticketId and visitedAt
    _id: ObjectId("66f0c2000000000000000003"), eventId: NumberLong("1"), eventTitle: "Осінній концерт ЧНУ", kind: "verified-review",
    author: olena, rating: NumberInt(5), text: "Чудовий звук і атмосфера", ticketId: "7d2f6c1e", visitedAt: ISODate("2026-10-14T18:00:00Z"),
    comments: [], createdAt: ISODate("2026-10-15T08:00:00Z"), version: NumberInt(1),
  },
  {
    _id: ObjectId("66f0c2000000000000000004"), eventId: NumberLong("1"), eventTitle: "Осінній концерт ЧНУ", kind: "review",
    author: ihor, rating: NumberInt(3), text: "Довго чекали на вході", comments: [], discussionId: concertTalk,
    createdAt: ISODate("2026-10-15T09:00:00Z"), version: NumberInt(1),
  },
]);

// Computed pattern: eventRatings is the result of an aggregation pipeline
database.reviews.aggregate([
  { $group: { _id: "$eventId", eventTitle: { $first: "$eventTitle" }, avg: { $avg: "$rating" }, count: { $sum: 1 }, ratings: { $push: "$rating" } } },
  {
    $project: {
      eventTitle: 1, avg: { $round: ["$avg", 2] }, count: 1,
      distribution: {
        $arrayToObject: {
          $map: {
            input: [1, 2, 3, 4, 5],
            as: "r",
            in: { k: { $toString: "$$r" }, v: { $size: { $filter: { input: "$ratings", cond: { $eq: ["$$this", "$$r"] } } } } },
          },
        },
      },
    },
  },
  { $merge: { into: "eventRatings", whenMatched: "replace", whenNotMatched: "insert" } },
]);

print("reviews: " + database.reviews.countDocuments() + ", discussions: " + database.discussions.countDocuments()
  + ", messages: " + database.messages.countDocuments() + ", eventRatings: " + database.eventRatings.countDocuments());
