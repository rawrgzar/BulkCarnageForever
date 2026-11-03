using BulkCarnageForever.V10.Core.Model;
using Microsoft.EntityFrameworkCore;

namespace BulkCarnageForever.V10.DAL;

/// <summary>
/// Local, owner-scoped Ghost management. Returned profiles are detached snapshots.
/// Schedule edits affect settings only; this store never changes diary records or simulations.
/// </summary>
public sealed class GhostProfileStore(AppDbContext database)
{
    private readonly AppDbContext _db = database ?? throw new ArgumentNullException(nameof(database));

    public async Task<List<GhostProfile>> ListAsync(long userProfileId, CancellationToken ct = default)
    {
        await RequireOwnerAsync(userProfileId, ct);
        return await Owned(userProfileId).OrderByDescending(x => x.IsDefault)
            .ThenBy(x => x.Name).ThenBy(x => x.Id).ToListAsync(ct);
    }

    public async Task<GhostProfile> EnsureDefaultAsync(long userProfileId, CancellationToken ct = default)
    {
        await RequireOwnerAsync(userProfileId, ct);
        var ghost = new GhostProfile { UserProfileId = userProfileId, IsDefault = true };
        // One atomic insert joins any caller transaction. The filtered unique index also
        // prevents duplicate defaults when independent contexts initialize the same user.
        await _db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "GhostProfiles"
                ("Id", "UserProfileId", "Name", "CreatedAt", "IsDefault", "VisualIdentity",
                 "MondayMultiplier", "TuesdayMultiplier", "WednesdayMultiplier", "ThursdayMultiplier",
                 "FridayMultiplier", "SaturdayMultiplier", "SundayMultiplier")
            VALUES ({ghost.Id}, {userProfileId}, {ghost.Name}, {ghost.CreatedAt}, 1, NULL, 1, 1, 1, 1, 1, 1, 1)
            ON CONFLICT ("UserProfileId") WHERE "IsDefault" = 1 DO NOTHING;
            """, ct);
        return await Owned(userProfileId).SingleAsync(x => x.IsDefault, ct);
    }

    public async Task<GhostProfile> CreateAsync(long userProfileId, string name, CancellationToken ct = default)
    {
        name = ValidateName(name);
        await RequireOwnerAsync(userProfileId, ct);
        return await AddAsync(new GhostProfile { UserProfileId = userProfileId, Name = name }, ct);
    }

    public async Task<GhostProfile> DuplicateAsync(long userProfileId, Guid sourceId, string name, CancellationToken ct = default)
    {
        name = ValidateName(name);
        var source = await RequireGhostAsync(userProfileId, sourceId, ct);
        var copy = new GhostProfile { UserProfileId = userProfileId, Name = name };
        CopySettings(source, copy);
        return await AddAsync(copy, ct);
    }

    public async Task RenameAsync(long userProfileId, Guid ghostId, string name, CancellationToken ct = default)
    {
        name = ValidateName(name);
        await RequireGhostAsync(userProfileId, ghostId, ct);
        int changed = await Owned(userProfileId).Where(x => x.Id == ghostId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.Name, name), ct);
        RequireChanged(changed);
    }

    /// <summary>
    /// Copies only the seven ratios and optional visual identity. The supplied settings'
    /// identity, owner, name, creation timestamp, and default flag cannot replace saved metadata.
    /// Use RenameAsync to change the name.
    /// </summary>
    public async Task UpdateAsync(long userProfileId, Guid ghostId, GhostProfile settings, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ValidateSettings(settings);
        // Copy values before awaiting, so later edits to the input cannot bypass validation.
        var values = new GhostProfile();
        CopySettings(settings, values);
        await RequireGhostAsync(userProfileId, ghostId, ct);
        int changed = await Owned(userProfileId).Where(x => x.Id == ghostId).ExecuteUpdateAsync(setters => setters
            .SetProperty(x => x.MondayMultiplier, values.MondayMultiplier)
            .SetProperty(x => x.TuesdayMultiplier, values.TuesdayMultiplier)
            .SetProperty(x => x.WednesdayMultiplier, values.WednesdayMultiplier)
            .SetProperty(x => x.ThursdayMultiplier, values.ThursdayMultiplier)
            .SetProperty(x => x.FridayMultiplier, values.FridayMultiplier)
            .SetProperty(x => x.SaturdayMultiplier, values.SaturdayMultiplier)
            .SetProperty(x => x.SundayMultiplier, values.SundayMultiplier)
            .SetProperty(x => x.VisualIdentity, values.VisualIdentity), ct);
        RequireChanged(changed);
    }

    public async Task DeleteAsync(long userProfileId, Guid ghostId, CancellationToken ct = default)
    {
        var ghost = await RequireGhostAsync(userProfileId, ghostId, ct);
        if (ghost.IsDefault) throw new InvalidOperationException("The default Ghost cannot be deleted.");
        int changed = await Owned(userProfileId).Where(x => x.Id == ghostId && !x.IsDefault).ExecuteDeleteAsync(ct);
        RequireChanged(changed);
    }

    private IQueryable<GhostProfile> Owned(long userProfileId) =>
        _db.GhostProfiles.AsNoTracking().Where(x => x.UserProfileId == userProfileId);

    private async Task RequireOwnerAsync(long userProfileId, CancellationToken ct)
    {
        if (userProfileId <= 0 || !await _db.UserProfiles.AnyAsync(x => x.Id == userProfileId, ct))
            throw new ArgumentException("An existing user profile is required.", nameof(userProfileId));
    }

    private async Task<GhostProfile> RequireGhostAsync(long userProfileId, Guid ghostId, CancellationToken ct)
    {
        await RequireOwnerAsync(userProfileId, ct);
        return await Owned(userProfileId).SingleOrDefaultAsync(x => x.Id == ghostId, ct)
            ?? throw new KeyNotFoundException("The Ghost was not found for this user.");
    }

    private async Task<GhostProfile> AddAsync(GhostProfile ghost, CancellationToken ct)
    {
        _db.GhostProfiles.Add(ghost);
        await _db.SaveChangesAsync(ct);
        _db.Entry(ghost).State = EntityState.Detached;
        return ghost;
    }

    private static string ValidateName(string name)
    {
        name = name?.Trim() ?? "";
        if (name.Length is < 1 or > 80)
            throw new ArgumentException("Enter a Ghost name from 1 to 80 characters.", nameof(name));
        return name;
    }

    private static void ValidateSettings(GhostProfile settings)
    {
        foreach (var day in Enum.GetValues<DayOfWeek>())
        {
            double value = settings.GetMultiplier(day);
            if (!double.IsFinite(value) || value < 0)
                throw new ArgumentException("Each multiplier must be finite and nonnegative.", nameof(settings));
        }
    }

    private static void CopySettings(GhostProfile source, GhostProfile target)
    {
        target.MondayMultiplier = source.MondayMultiplier;
        target.TuesdayMultiplier = source.TuesdayMultiplier;
        target.WednesdayMultiplier = source.WednesdayMultiplier;
        target.ThursdayMultiplier = source.ThursdayMultiplier;
        target.FridayMultiplier = source.FridayMultiplier;
        target.SaturdayMultiplier = source.SaturdayMultiplier;
        target.SundayMultiplier = source.SundayMultiplier;
        target.VisualIdentity = source.VisualIdentity;
    }

    private static void RequireChanged(int changed)
    {
        if (changed == 0) throw new KeyNotFoundException("The Ghost was not found for this user.");
    }
}
