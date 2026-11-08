namespace Reviews.WebApi.Controllers;

// HTTP bodies for routes where the id comes from the URL
public record UpdateReviewRequest(int Rating, string Text, int Version);

public record AddCommentRequest(string AuthorUserId, string AuthorName, string Text);
