using Exb.Data.Entities;

namespace Exb.Web.Models;

public sealed record SelectOption(int Id, string Label);

/// <summary>
/// The fields of one programme session, for the shared add/edit partial.
///
/// Add and edit are the same form because they are the same thing: a session
/// added at nine in the morning is usually edited by ten, and two forms that
/// drift apart is how a field ends up editable in one place and not the other.
/// <see cref="Prefix"/> keeps the element ids unique when the page renders the
/// form once per row.
/// </summary>
public sealed class SessionFormModel
{
    public string Prefix { get; init; } = "new";

    public string? Title { get; init; }
    public SessionKind Kind { get; init; } = SessionKind.Lecture;
    public string? SpeakerName { get; init; }
    public string? SpeakerTitle { get; init; }
    public string? SpeakerOrganisation { get; init; }
    public string? Summary { get; init; }

    public DateOnly EventDate { get; init; }
    public TimeOnly StartsAt { get; init; }
    public TimeOnly EndsAt { get; init; }

    public int? HallId { get; init; }
    public string? RoomName { get; init; }
    public int? CategoryId { get; init; }
    public int? SubCategoryId { get; init; }

    public int Capacity { get; init; }
    public bool RequiresBooking { get; init; }
    public string? Language { get; init; }

    public IReadOnlyList<SelectOption> Halls { get; init; } = [];
    public IReadOnlyList<SelectOption> Categories { get; init; } = [];
    public IReadOnlyList<SelectOption> SubCategories { get; init; } = [];
}
