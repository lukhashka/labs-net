using AutoMapper;
using MongoDB.Bson;
using Reviews.Application.Common.Models;
using Reviews.Domain.Entities;
using Reviews.Domain.ValueObjects;

namespace Reviews.Application.Common.Mappings;

// AutoMapper 14.0.0 — last MIT version, works without a license key.
// Maps ObjectId -> string, value objects -> primitives, embedded comments[] -> CommentDto[].
public class ReviewsProfile : Profile
{
    public ReviewsProfile()
    {
        CreateMap<ObjectId, string>().ConvertUsing(id => id.ToString());
        CreateMap<Rating, int>().ConvertUsing(r => r.Value);

        CreateMap<Comment, CommentDto>()
            .ForCtorParam(nameof(CommentDto.AuthorUserId), o => o.MapFrom(c => c.Author.UserId))
            .ForCtorParam(nameof(CommentDto.AuthorName), o => o.MapFrom(c => c.Author.DisplayName));

        CreateMap<Review, ReviewDto>()
            .ForCtorParam(nameof(ReviewDto.AuthorUserId), o => o.MapFrom(r => r.Author.UserId))
            .ForCtorParam(nameof(ReviewDto.AuthorName), o => o.MapFrom(r => r.Author.DisplayName))
            .ForCtorParam(nameof(ReviewDto.CommentsCount), o => o.MapFrom(r => r.Comments.Count));

        CreateMap<Review, ReviewDetailsDto>()
            .ForCtorParam(nameof(ReviewDetailsDto.AuthorUserId), o => o.MapFrom(r => r.Author.UserId))
            .ForCtorParam(nameof(ReviewDetailsDto.AuthorName), o => o.MapFrom(r => r.Author.DisplayName))
            .ForCtorParam(nameof(ReviewDetailsDto.Comments), o => o.MapFrom(r => r.Comments));

        CreateMap<Discussion, DiscussionDto>()
            .ForCtorParam(nameof(DiscussionDto.StartedBy), o => o.MapFrom(d => d.StartedBy.DisplayName));
    }
}
