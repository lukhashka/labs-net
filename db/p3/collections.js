// CampusTix · ReviewsDb (Project #3, Lab 01 -> Lab 04). Collections, JSON Schema validation, indexes.
// Idempotent. Run first:  mongosh "mongodb://localhost:27017/ReviewsDb" collections.js

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
