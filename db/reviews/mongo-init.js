// CampusTix · ReviewsDb (Project #3, Lab 01 -> Lab 04). Collections, JSON Schema validation, indexes, seed data.
// Idempotent: run with  mongosh "mongodb://localhost:27017/ReviewsDb" mongo-init.js
// eventId values match CatalogDb (logical references, no constraints between databases).

const database = db.getSiblingDB("ReviewsDb");

function ensureCollection(name, schema) {
  if (database.getCollectionNames().includes(name)) {
    database.runCommand({ collMod: name, validator: { $jsonSchema: schema } });
  } else {
    database.createCollection(name, { validator: { $jsonSchema: schema } });
  }
}

const authorSchema = {
  bsonType: "object",
  required: ["userId", "displayName"],
  properties: { userId: { bsonType: "string" }, displayName: { bsonType: "string" } },
};

ensureCollection("reviews", {
  bsonType: "object",
  required: ["eventId", "eventTitle", "author", "rating", "text", "createdAt", "version"],
  properties: {
    eventId: { bsonType: ["int", "long"], minimum: 1 },
    eventTitle: { bsonType: "string" },
    author: authorSchema,
    rating: { bsonType: "int", minimum: 1, maximum: 5 },
    text: { bsonType: "string", maxLength: 2000 },
    photos: { bsonType: "array", items: { bsonType: "string" } },
    comments: { bsonType: "array" },
    discussionId: { bsonType: "objectId" },
    version: { bsonType: "int", minimum: 1 },
  },
});

ensureCollection("discussions", {
  bsonType: "object",
  required: ["eventId", "eventTitle", "title", "startedBy", "isClosed", "createdAt", "version"],
  properties: {
    eventId: { bsonType: ["int", "long"], minimum: 1 },
    title: { bsonType: "string", maxLength: 200 },
    startedBy: authorSchema,
    isClosed: { bsonType: "bool" },
    lastMessages: { bsonType: "array", maxItems: 3 },   // subset pattern: only the latest messages are embedded
  },
});

ensureCollection("messages", {
  bsonType: "object",
  required: ["discussionId", "author", "text", "createdAt"],
  properties: { discussionId: { bsonType: "objectId" }, author: authorSchema, text: { bsonType: "string" } },
});

ensureCollection("eventRatings", {
  bsonType: "object",
  required: ["eventTitle", "avg", "count", "distribution"],
  properties: { avg: { bsonType: ["double", "int"] }, count: { bsonType: "int" }, distribution: { bsonType: "object" } },
});

// Indexes. createIndex is idempotent.
database.reviews.createIndex({ eventId: 1, createdAt: -1 }, { name: "eventId_createdAt" });          // latest reviews of an event
database.reviews.createIndex({ text: "text" }, { name: "text_search" });                             // full-text search
database.reviews.createIndex({ eventId: 1, "author.userId": 1 }, { name: "eventId_author_unique", unique: true }); // one review per author
database.discussions.createIndex({ eventId: 1, createdAt: -1 }, { name: "eventId_createdAt" });      // discussions of an event
database.messages.createIndex({ discussionId: 1, createdAt: 1 }, { name: "discussionId_createdAt" }); // pages of a long discussion

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
