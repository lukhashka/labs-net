using FluentValidation;
using MediatR;
using Reviews.Application.Common.Messaging;
using Reviews.Domain.Entities;
using Reviews.Domain.Interfaces;
using Reviews.Domain.ValueObjects;

namespace Reviews.Application.Discussions.Commands.CreateDiscussion;

public record CreateDiscussionCommand(
    long EventId, string EventTitle, string Title, string AuthorUserId, string AuthorName) : ICommand<string>;

public class CreateDiscussionCommandHandler(IDiscussionRepository discussions)
    : IRequestHandler<CreateDiscussionCommand, string>
{
    public async Task<string> Handle(CreateDiscussionCommand cmd, CancellationToken ct)
    {
        var discussion = new Discussion(
            cmd.EventId, cmd.EventTitle, cmd.Title, new Author(cmd.AuthorUserId, cmd.AuthorName));
        await discussions.AddAsync(discussion, ct);
        return discussion.Id.ToString();
    }
}

public class CreateDiscussionCommandValidator : AbstractValidator<CreateDiscussionCommand>
{
    public CreateDiscussionCommandValidator()
    {
        RuleFor(x => x.EventId).GreaterThan(0);
        RuleFor(x => x.EventTitle).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Title).NotEmpty().MaximumLength(200);
        RuleFor(x => x.AuthorUserId).NotEmpty();
        RuleFor(x => x.AuthorName).NotEmpty().MaximumLength(100);
    }
}
