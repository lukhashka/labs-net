using Catalog.Bll.Dtos;
using FluentValidation;

namespace Catalog.Bll.Validation;

// FluentValidation holds rules that DataAnnotations can't express (cross-field, formats, ranges).
// Required/StringLength are NOT repeated here — they live on the DTOs as DataAnnotations.
// Registered via AddValidatorsFromAssemblyContaining and called explicitly in controllers.

public class CreateEventDtoValidator : AbstractValidator<CreateEventDto>
{
    public CreateEventDtoValidator()
    {
        RuleFor(x => x.Slug).Matches("^[a-z0-9-]+$")
            .WithMessage("Slug may contain only lowercase latin letters, digits and '-'");
        RuleFor(x => x.EndsAt).GreaterThan(x => x.StartsAt)
            .WithMessage("EndsAt must be later than StartsAt");
        RuleFor(x => x.AgeLimit).InclusiveBetween(0, 21);
        RuleForEach(x => x.CategoryIds).GreaterThan(0);
    }
}

public class CreateTicketTypeDtoValidator : AbstractValidator<CreateTicketTypeDto>
{
    public CreateTicketTypeDtoValidator()
    {
        RuleFor(x => x.Code).Matches("^[A-Z0-9-]+$")
            .WithMessage("Code may contain only uppercase latin letters, digits and '-'");
        RuleFor(x => x.Price).GreaterThan(0).PrecisionScale(18, 2, true);
        RuleFor(x => x.Capacity).GreaterThan(0).LessThanOrEqualTo(10000);
    }
}

public class ChangePriceDtoValidator : AbstractValidator<ChangePriceDto>
{
    public ChangePriceDtoValidator()
    {
        RuleFor(x => x.Price).GreaterThan(0).PrecisionScale(18, 2, true);
    }
}

public class EventQueryValidator : AbstractValidator<EventQuery>
{
    private static readonly string[] SortFields = ["startsAt", "title", "price"];

    public EventQueryValidator()
    {
        RuleFor(x => x.Page).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageSize).InclusiveBetween(1, Paging.MaxPageSize);
        RuleFor(x => x.SortBy).Must(s => SortFields.Contains(s, StringComparer.OrdinalIgnoreCase))
            .WithMessage("SortBy must be one of: startsAt, title, price");
        RuleFor(x => x.SortDir).Must(s => s is "asc" or "desc").WithMessage("SortDir must be 'asc' or 'desc'");
        RuleFor(x => x.MaxPrice).GreaterThanOrEqualTo(x => x.MinPrice).When(x => x.MinPrice is not null && x.MaxPrice is not null);
        RuleFor(x => x.To).GreaterThanOrEqualTo(x => x.From).When(x => x.From is not null && x.To is not null);
    }
}
