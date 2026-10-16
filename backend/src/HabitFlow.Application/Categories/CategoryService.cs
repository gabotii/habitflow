using FluentValidation;
using HabitFlow.Application.Abstractions;
using HabitFlow.Application.Common;
using HabitFlow.Domain.Habits;
using Microsoft.EntityFrameworkCore;

namespace HabitFlow.Application.Categories;

public sealed record CategoryDto(Guid Id, string Name, HabitColor Color, string Icon, bool IsBuiltIn, int HabitCount);

public sealed record SaveCategoryRequest(string Name, HabitColor Color, string Icon);

public sealed class SaveCategoryRequestValidator : AbstractValidator<SaveCategoryRequest>
{
    public SaveCategoryRequestValidator()
    {
        RuleFor(r => r.Name).NotEmpty().MaximumLength(HabitCategory.NameMaxLength);
        RuleFor(r => r.Color).IsInEnum();
        RuleFor(r => r.Icon).Must(HabitIcons.All.Contains).WithMessage("Icono no válido.");
    }
}

/// <summary>Built-in categories are visible to everyone and read-only; users can add their own.</summary>
public sealed class CategoryService(IAppDbContext db, ICurrentUser currentUser)
{
    public const int MaxCustomCategories = 50;

    public async Task<IReadOnlyList<CategoryDto>> ListAsync(CancellationToken ct) =>
        await db.Categories
            .OrderBy(c => c.SortOrder).ThenBy(c => c.Name)
            .Select(c => new CategoryDto(c.Id, c.Name, c.Color, c.Icon, c.UserId == null,
                db.Habits.Count(h => h.CategoryId == c.Id)))
            .ToListAsync(ct);

    public async Task<CategoryDto> CreateAsync(SaveCategoryRequest request, CancellationToken ct)
    {
        if (await db.Categories.CountAsync(c => c.UserId != null, ct) >= MaxCustomCategories)
        {
            throw new ConflictException("category.limit", $"Puedes crear como máximo {MaxCustomCategories} categorías.");
        }

        await EnsureUniqueNameAsync(request.Name, null, ct);
        var category = HabitCategory.Create(currentUser.UserId, request.Name, request.Color, request.Icon);
        db.Categories.Add(category);
        await db.SaveChangesAsync(ct);
        return new CategoryDto(category.Id, category.Name, category.Color, category.Icon, false, 0);
    }

    public async Task<CategoryDto> UpdateAsync(Guid id, SaveCategoryRequest request, CancellationToken ct)
    {
        var category = await db.Categories.SingleOrDefaultAsync(c => c.Id == id, ct)
                       ?? throw new NotFoundException("Categoría");
        await EnsureUniqueNameAsync(request.Name, id, ct);
        category.Update(request.Name, request.Color, request.Icon);
        await db.SaveChangesAsync(ct);
        var count = await db.Habits.CountAsync(h => h.CategoryId == id, ct);
        return new CategoryDto(category.Id, category.Name, category.Color, category.Icon, category.IsBuiltIn, count);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct)
    {
        var category = await db.Categories.SingleOrDefaultAsync(c => c.Id == id, ct)
                       ?? throw new NotFoundException("Categoría");
        if (category.IsBuiltIn)
        {
            throw new ConflictException("category.built_in", "Las categorías predeterminadas no se pueden eliminar.");
        }

        if (await db.Habits.AnyAsync(h => h.CategoryId == id, ct))
        {
            throw new ConflictException("category.in_use",
                "La categoría tiene hábitos (incluidos archivados). Muévelos a otra categoría antes de eliminarla.");
        }

        db.Categories.Remove(category);
        await db.SaveChangesAsync(ct);
    }

    private async Task EnsureUniqueNameAsync(string name, Guid? exceptId, CancellationToken ct)
    {
        var normalized = HabitCategory.Normalize(name);
        if (await db.Categories.AnyAsync(c => c.NormalizedName == normalized && c.Id != exceptId, ct))
        {
            throw new ConflictException("category.duplicate_name", "Ya existe una categoría con ese nombre.");
        }
    }
}
