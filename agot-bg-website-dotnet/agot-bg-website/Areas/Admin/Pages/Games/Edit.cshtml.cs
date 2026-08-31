using System.Text.Json;
using agot_bg_website.Data;
using agot_bg_website.Domain;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace agot_bg_website.Areas.Admin.Pages.Games;

/// <summary>
/// Raw admin editor for a single game — the .NET equivalent of what Django Admin's default
/// model-edit form gave for free, and the moderation tool this maintainer has repeatedly needed
/// to hand-edit serialized_game or ban/punish players directly in the database (see chat history).
/// </summary>
public class EditModel(ApplicationDbContext db) : PageModel
{
    public Game GameEntity { get; set; } = null!;

    [BindProperty]
    public string Name { get; set; } = "";

    [BindProperty]
    public GameState State { get; set; }

    [BindProperty]
    public string? SerializedGameJson { get; set; }

    [BindProperty]
    public string? ViewOfGameJson { get; set; }

    [TempData]
    public string? StatusMessage { get; set; }

    public async Task<IActionResult> OnGetAsync(Guid id)
    {
        var game = await db.Games.Include(g => g.OwnerUser).FirstOrDefaultAsync(g => g.Id == id);
        if (game is null)
        {
            return NotFound();
        }

        GameEntity = game;
        Name = game.Name;
        State = game.State;
        SerializedGameJson = Format(game.SerializedGame);
        ViewOfGameJson = Format(game.ViewOfGame);
        return Page();
    }

    public async Task<IActionResult> OnPostSaveAsync(Guid id)
    {
        var game = await db.Games.FirstOrDefaultAsync(g => g.Id == id);
        if (game is null)
        {
            return NotFound();
        }

        GameEntity = game;

        if (string.IsNullOrWhiteSpace(Name))
        {
            ModelState.AddModelError(nameof(Name), "Name is required.");
        }

        JsonDocument? serializedGame = null;
        if (!string.IsNullOrWhiteSpace(SerializedGameJson))
        {
            try
            {
                serializedGame = JsonDocument.Parse(SerializedGameJson);
            }
            catch (JsonException ex)
            {
                ModelState.AddModelError(nameof(SerializedGameJson), $"Invalid JSON: {ex.Message}");
            }
        }

        JsonDocument? viewOfGame = null;
        if (!string.IsNullOrWhiteSpace(ViewOfGameJson))
        {
            try
            {
                viewOfGame = JsonDocument.Parse(ViewOfGameJson);
            }
            catch (JsonException ex)
            {
                ModelState.AddModelError(nameof(ViewOfGameJson), $"Invalid JSON: {ex.Message}");
            }
        }

        if (!ModelState.IsValid)
        {
            return Page();
        }

        game.Name = Name;
        game.State = State;
        game.SerializedGame = serializedGame;
        game.ViewOfGame = viewOfGame;
        game.UpdatedAt = DateTimeOffset.UtcNow;

        await db.SaveChangesAsync();

        StatusMessage = $"Game '{game.Name}' saved. Note: if the game server has this game loaded " +
            "in memory, it will overwrite this on its next save — restart/reload the game server " +
            "session first if you need this edit to stick.";
        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostCancelGameAsync(Guid id)
    {
        var game = await db.Games.FirstOrDefaultAsync(g => g.Id == id);
        if (game is null)
        {
            return NotFound();
        }

        game.State = GameState.Cancelled;
        game.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync();

        StatusMessage = $"Game '{game.Name}' cancelled.";
        return RedirectToPage(new { id });
    }

    private static string? Format(JsonDocument? doc)
    {
        if (doc is null)
        {
            return null;
        }

        return JsonSerializer.Serialize(doc.RootElement, new JsonSerializerOptions { WriteIndented = true });
    }
}
