// CampusTix · ReviewsDb · example queries and aggregations
//   mongosh "mongodb://localhost:27017/ReviewsDb" queries.js
const database = db.getSiblingDB("ReviewsDb");
const show = (title, value) => { print("\n--- " + title); printjson(value); };

show("1. Documents with different sets of fields in one collection",
  database.reviews.find({}, { _id: 0, eventId: 1, kind: 1, photos: 1, ticketId: 1, discussionId: 1 }).toArray());

show("2. Latest reviews of an event (index eventId_createdAt)",
  database.reviews.find({ eventId: 2 }, { _id: 0, "author.displayName": 1, rating: 1, text: 1 }).sort({ createdAt: -1 }).limit(10).toArray());

show("3. Full-text search",
  database.reviews.find({ $text: { $search: "хакатон" } }, { _id: 0, text: 1 }).toArray());

show("4. Reference: review -> discussion ($lookup)",
  database.reviews.aggregate([
    { $match: { discussionId: { $exists: true } } },
    { $lookup: { from: "discussions", localField: "discussionId", foreignField: "_id", as: "discussion" } },
    { $project: { _id: 0, text: 1, discussionTitle: { $first: "$discussion.title" } } },
  ]).toArray());

show("5. Hybrid: embedded latest messages + count in the separate collection",
  database.discussions.aggregate([
    { $lookup: { from: "messages", localField: "_id", foreignField: "discussionId", as: "all" } },
    { $project: { _id: 0, title: 1, embedded: { $size: { $ifNull: ["$lastMessages", []] } }, total: { $size: "$all" } } },
  ]).toArray());

show("6. Computed collection eventRatings", database.eventRatings.find().toArray());

// A document that breaks the JSON Schema must be rejected
try {
  database.reviews.insertOne({ eventId: 1, eventTitle: "x", author: { userId: "u", displayName: "d" }, rating: 9, text: "bad", createdAt: new Date(), version: 1 });
  print("\nFAILED: invalid document was accepted");
} catch (e) {
  print("\n--- 7. Validation rejected a document with rating 9: " + e.code + " (DocumentValidationFailure)");
}
