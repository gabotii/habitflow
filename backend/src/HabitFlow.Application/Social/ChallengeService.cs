using FluentValidation;
using HabitFlow.Application.Abstractions;
using HabitFlow.Application.Common;
using HabitFlow.Domain.Common;
using HabitFlow.Domain.Habits;
using HabitFlow.Domain.Social;
using Microsoft.EntityFrameworkCore;

namespace HabitFlow.Application.Social;

public interface IChallengeFields
{
    string Name { get; }
    string? Description { get; }
    DateOnly StartDate { get; }
    DateOnly EndDate { get; }
    ChallengeRule Rule { get; }
    decimal? TargetValue { get; }
    string? Unit { get; }
}

public sealed record CreateChallengeRequest(
    string Name,
    string? Description,
    DateOnly StartDate,
    DateOnly EndDate,
    ChallengeRule Rule,
    decimal? TargetValue,
    string? Unit,
    // Optional: the creator joins right away with this habit.
    Guid? HabitId) : IChallengeFields;

/// <summary>Full replacement. Rules that differ from the current ones are only accepted while still editable.</summary>
public sealed record UpdateChallengeRequest(
    string Name,
    string? Description,
    DateOnly StartDate,
    DateOnly EndDate,
    ChallengeRule Rule,
    decimal? TargetValue,
    string? Unit) : IChallengeFields;

public sealed record JoinChallengeRequest(string Code, Guid HabitId);

public sealed record ChallengeSummaryDto(
    Guid Id,
    string Name,
    DateOnly StartDate,
    DateOnly EndDate,
    ChallengeRule Rule,
    decimal TargetValue,
    string? Unit,
    ChallengePhase Phase,
    int Participants,
    bool IsCreator,
    ChallengeStandingDto? Me);

/// <summary>What others see about a participant: a name and numbers — never the habit or its notes.</summary>
public sealed record ChallengeStandingDto(
    string DisplayName,
    bool IsMe,
    decimal Progress,
    decimal Percent,
    ChallengeResult Result,
    DateOnly? LeftOn);

public sealed record ChallengeDto(
    Guid Id,
    string Name,
    string? Description,
    DateOnly StartDate,
    DateOnly EndDate,
    ChallengeRule Rule,
    decimal TargetValue,
    string? Unit,
    ChallengePhase Phase,
    string JoinCode,
    bool IsCreator,
    bool CanDelete,
    // Creator only: dates, rule, target and unit can still change (before the start, nobody else joined).
    bool CanEditRules,
    // The caller's own linked habit (only visible to them).
    ChallengeHabitDto? MyHabit,
    IReadOnlyList<ChallengeStandingDto> Standings);

public sealed record ChallengeHabitDto(Guid Id, string Name);

public sealed record ChallengePreviewDto(
    Guid Id,
    string Name,
    string? Description,
    DateOnly StartDate,
    DateOnly EndDate,
    ChallengeRule Rule,
    decimal TargetValue,
    string? Unit,
    ChallengePhase Phase,
    int Participants,
    bool AlreadyJoined);

public sealed class CreateChallengeRequestValidator : ChallengeFieldsValidator<CreateChallengeRequest>;

public sealed class UpdateChallengeRequestValidator : ChallengeFieldsValidator<UpdateChallengeRequest>;

public abstract class ChallengeFieldsValidator<T> : AbstractValidator<T> where T : IChallengeFields
{
    protected ChallengeFieldsValidator()
    {
        RuleFor(r => r.Name).NotEmpty().MaximumLength(Challenge.NameMaxLength);
        RuleFor(r => r.Description).MaximumLength(Challenge.DescriptionMaxLength);
        RuleFor(r => r.Rule).IsInEnum();
        RuleFor(r => r.EndDate).GreaterThanOrEqualTo(r => r.StartDate)
            .WithMessage("La fecha final no puede ser anterior a la inicial.");
        RuleFor(r => r.TargetValue!.Value).Amount(1_000_000).OverridePropertyName("targetValue")
            .When(r => r.Rule != ChallengeRule.DailyCompletion);
        RuleFor(r => r.TargetValue).NotNull().When(r => r.Rule != ChallengeRule.DailyCompletion)
            .WithMessage("Indica la meta del reto.");
        RuleFor(r => r.Unit).NotEmpty().MaximumLength(Habit.UnitMaxLength)
            .When(r => r.Rule == ChallengeRule.QuantityTotal);
    }
}

public sealed class JoinChallengeRequestValidator : AbstractValidator<JoinChallengeRequest>
{
    public JoinChallengeRequestValidator()
    {
        RuleFor(r => r.Code).NotEmpty().Length(8);
        RuleFor(r => r.HabitId).NotEmpty();
    }
}

/// <summary>
/// Challenges are shared between users, so they are not covered by the ownership filters: every method checks that
/// the caller takes part (or created the challenge). Progress of others is computed from their logs with an explicit
/// opt-out of the filters, restricted to the habit they linked and projected to numbers only.
/// </summary>
public sealed class ChallengeService(
    IAppDbContext db,
    ICurrentUser currentUser,
    IUserClock clock,
    IUserDirectory users)
{
    public const int MaxCreatedPerUser = 50;

    public async Task<IReadOnlyList<ChallengeSummaryDto>> ListAsync(CancellationToken ct)
    {
        var me = currentUser.UserId;
        var challenges = await db.Challenges.AsNoTracking().Include(c => c.Participants)
            .Where(c => c.CreatorId == me || c.Participants.Any(p => p.UserId == me))
            .OrderByDescending(c => c.StartDate)
            .ToListAsync(ct);
        var today = await clock.GetTodayAsync(ct);
        var progress = await ProgressAsync(challenges, ct);
        return challenges.Select(c =>
        {
            var mine = c.Participants.SingleOrDefault(p => p.UserId == me);
            return new ChallengeSummaryDto(c.Id, c.Name, c.StartDate, c.EndDate, c.Rule, c.TargetValue, c.Unit,
                c.PhaseOn(today), c.Participants.Count(p => p.LeftOn is null), c.CreatorId == me,
                mine is null ? null : Standing(c, mine, progress, today, "Tú", true));
        }).ToList();
    }

    public async Task<ChallengeDto> GetAsync(Guid id, CancellationToken ct)
    {
        var challenge = await LoadVisibleAsync(id, ct);
        var me = currentUser.UserId;
        var today = await clock.GetTodayAsync(ct);
        var progress = await ProgressAsync([challenge], ct);
        var names = await users.GetDisplayNamesAsync(challenge.Participants.Select(p => p.UserId).ToList(), ct);
        var mine = challenge.Participants.SingleOrDefault(p => p.UserId == me);
        var myHabit = mine?.HabitId is { } habitId
            ? await db.Habits.AsNoTracking().Where(h => h.Id == habitId)
                .Select(h => new ChallengeHabitDto(h.Id, h.Name)).SingleOrDefaultAsync(ct)
            : null;

        var standings = challenge.Participants
            .Select(p => Standing(challenge, p, progress, today, names.GetValueOrDefault(p.UserId, "Participante"),
                p.UserId == me))
            .OrderBy(s => s.Result == ChallengeResult.Abandoned)
            .ThenByDescending(s => s.Progress)
            .ThenBy(s => s.DisplayName)
            .ToList();

        return new ChallengeDto(challenge.Id, challenge.Name, challenge.Description, challenge.StartDate,
            challenge.EndDate, challenge.Rule, challenge.TargetValue, challenge.Unit, challenge.PhaseOn(today),
            challenge.JoinCode, challenge.CreatorId == me, CanDelete(challenge),
            challenge.CreatorId == me && challenge.RulesEditableOn(today), myHabit, standings);
    }

    /// <summary>Creator only. See <see cref="Challenge.Update"/> for what can change and when.</summary>
    public async Task<ChallengeDto> UpdateAsync(Guid id, UpdateChallengeRequest request, CancellationToken ct)
    {
        var challenge = await LoadVisibleAsync(id, ct, tracked: true);
        var me = currentUser.UserId;
        if (challenge.CreatorId != me)
        {
            throw new DomainException("challenge.creator_only", "Solo quien creó el reto puede editarlo.");
        }

        var myHabit = challenge.Participants.SingleOrDefault(p => p.UserId == me && p.LeftOn == null)?.HabitId is { } habitId
            ? await db.Habits.AsNoTracking().SingleOrDefaultAsync(h => h.Id == habitId, ct)
            : null;
        challenge.Update(request.Name, request.Description, request.StartDate, request.EndDate, request.Rule,
            request.TargetValue, request.Unit, await clock.GetTodayAsync(ct), myHabit);
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    public async Task<ChallengeDto> CreateAsync(CreateChallengeRequest request, CancellationToken ct)
    {
        var me = currentUser.UserId;
        if (await db.Challenges.CountAsync(c => c.CreatorId == me, ct) >= MaxCreatedPerUser)
        {
            throw new ConflictException("challenge.limit", $"Puedes crear como máximo {MaxCreatedPerUser} retos.");
        }

        var today = await clock.GetTodayAsync(ct);
        var challenge = Challenge.Create(me, request.Name, request.Description, request.StartDate, request.EndDate,
            request.Rule, request.TargetValue, request.Unit, today);
        if (request.HabitId is { } habitId)
        {
            challenge.Join(me, await OwnHabitAsync(habitId, ct), today, clock.UtcNow);
        }

        db.Challenges.Add(challenge);
        await db.SaveChangesAsync(ct);
        return await GetAsync(challenge.Id, ct);
    }

    /// <summary>Anyone with the code can see what they would join (no participant details).</summary>
    public async Task<ChallengePreviewDto> PreviewAsync(string code, CancellationToken ct)
    {
        var normalized = code.Trim().ToUpperInvariant();
        var challenge = await db.Challenges.AsNoTracking().Include(c => c.Participants)
                            .SingleOrDefaultAsync(c => c.JoinCode == normalized, ct)
                        ?? throw new NotFoundException("Reto");
        var today = await clock.GetTodayAsync(ct);
        return new ChallengePreviewDto(challenge.Id, challenge.Name, challenge.Description, challenge.StartDate,
            challenge.EndDate, challenge.Rule, challenge.TargetValue, challenge.Unit, challenge.PhaseOn(today),
            challenge.Participants.Count(p => p.LeftOn is null),
            challenge.Participants.Any(p => p.UserId == currentUser.UserId));
    }

    public async Task<ChallengeDto> JoinAsync(JoinChallengeRequest request, CancellationToken ct)
    {
        var normalized = request.Code.Trim().ToUpperInvariant();
        var challenge = await db.Challenges.Include(c => c.Participants)
                            .SingleOrDefaultAsync(c => c.JoinCode == normalized, ct)
                        ?? throw new NotFoundException("Reto");
        var habit = await OwnHabitAsync(request.HabitId, ct);
        challenge.Join(currentUser.UserId, habit, await clock.GetTodayAsync(ct), clock.UtcNow);
        await db.SaveChangesAsync(ct);
        return await GetAsync(challenge.Id, ct);
    }

    public async Task<ChallengeDto> LeaveAsync(Guid id, CancellationToken ct)
    {
        var challenge = await LoadVisibleAsync(id, ct, tracked: true);
        challenge.Leave(currentUser.UserId, await clock.GetTodayAsync(ct));
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    /// <summary>The creator can delete a challenge nobody else has joined.</summary>
    public async Task DeleteAsync(Guid id, CancellationToken ct)
    {
        var challenge = await LoadVisibleAsync(id, ct, tracked: true);
        if (challenge.CreatorId != currentUser.UserId)
        {
            throw new DomainException("challenge.creator_only", "Solo quien creó el reto puede eliminarlo.");
        }

        if (!CanDelete(challenge))
        {
            throw new DomainException("challenge.has_participants",
                "No se puede eliminar un reto al que ya se han unido otras personas.");
        }

        db.Challenges.Remove(challenge);
        await db.SaveChangesAsync(ct);
    }

    private bool CanDelete(Challenge c) =>
        c.CreatorId == currentUser.UserId && c.Participants.All(p => p.UserId == currentUser.UserId);

    private static ChallengeStandingDto Standing(Challenge c, ChallengeParticipant p,
        IReadOnlyDictionary<Guid, decimal> progress, DateOnly today, string name, bool isMe)
    {
        var value = progress.GetValueOrDefault(p.Id);
        var percent = c.TargetValue <= 0 ? 0 : Math.Min(100m, Math.Round(value / c.TargetValue * 100m, 1));
        return new ChallengeStandingDto(name, isMe, value, percent, c.ResultFor(p, value, today), p.LeftOn);
    }

    /// <summary>
    /// Each participant's progress from their own linked habit between the start and their cut-off date. The query
    /// opts out of the ownership filter but matches both the habit and its owner, and reads numbers only.
    /// </summary>
    private async Task<IReadOnlyDictionary<Guid, decimal>> ProgressAsync(IReadOnlyCollection<Challenge> challenges,
        CancellationToken ct)
    {
        var participants = challenges.SelectMany(c => c.Participants.Where(p => p.HabitId != null)
            .Select(p => (Challenge: c, Participant: p))).ToList();
        if (participants.Count == 0)
        {
            return new Dictionary<Guid, decimal>();
        }

        var habitIds = participants.Select(x => x.Participant.HabitId!.Value).Distinct().ToList();
        var from = challenges.Min(c => c.StartDate);
        var to = challenges.Max(c => c.EndDate);
        var logs = (await db.HabitLogs.IgnoreQueryFilters().AsNoTracking()
                .Where(l => habitIds.Contains(l.HabitId) && l.LocalDate >= from && l.LocalDate <= to)
                .Select(l => new { l.HabitId, l.UserId, l.LocalDate, l.Quantity, l.IsCompleted })
                .ToListAsync(ct))
            .ToLookup(l => l.HabitId);

        return participants.ToDictionary(x => x.Participant.Id, x =>
        {
            var (c, p) = x;
            var until = c.CountsUntil(p);
            var own = logs[p.HabitId!.Value].Where(l => l.UserId == p.UserId && l.LocalDate >= c.StartDate &&
                                                        l.LocalDate <= until);
            return c.Rule == ChallengeRule.QuantityTotal
                ? own.Sum(l => l.Quantity ?? 0)
                : own.Count(l => l.IsCompleted);
        });
    }

    private async Task<Habit> OwnHabitAsync(Guid habitId, CancellationToken ct) =>
        await db.Habits.AsNoTracking().SingleOrDefaultAsync(h => h.Id == habitId, ct)
        ?? throw new DomainException("challenge.invalid_habit", "Elige uno de tus hábitos activos.");

    private async Task<Challenge> LoadVisibleAsync(Guid id, CancellationToken ct, bool tracked = false)
    {
        var me = currentUser.UserId;
        var query = db.Challenges.Include(c => c.Participants).AsQueryable();
        if (!tracked)
        {
            query = query.AsNoTracking();
        }

        return await query.SingleOrDefaultAsync(
                   c => c.Id == id && (c.CreatorId == me || c.Participants.Any(p => p.UserId == me)), ct)
               ?? throw new NotFoundException("Reto");
    }
}
