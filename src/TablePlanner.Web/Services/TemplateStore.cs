using Microsoft.JSInterop;
using TablePlanner.Export.Printing;

namespace TablePlanner.Web.Services;

/// <summary>
/// Keeps the user's own print templates in the browser's localStorage, separately from projects, so they are
/// reused for every event. No entry means the built-in template is used.
/// </summary>
public sealed class TemplateStore(IJSRuntime js)
{
    private static string Key(PrintKind kind) => $"tableplanner:template:{kind.ToString().ToLowerInvariant()}";

    /// <summary>The user's template, or null when the built-in one is used.</summary>
    public async Task<string?> LoadAsync(PrintKind kind) => await js.InvokeAsync<string?>("localStorage.getItem", Key(kind));

    /// <summary>Saves the user's template. Throws <see cref="JSException"/> when browser storage is full.</summary>
    public Task SaveAsync(PrintKind kind, string template) => js.InvokeVoidAsync("localStorage.setItem", Key(kind), template).AsTask();

    /// <summary>Goes back to the built-in template.</summary>
    public Task ResetAsync(PrintKind kind) => js.InvokeVoidAsync("localStorage.removeItem", Key(kind)).AsTask();

    private const string DesignKey = "tableplanner:carddesign";

    /// <summary>The design last saved in the card designer, or null.</summary>
    public async Task<CardDesign?> LoadDesignAsync() => CardDesign.FromJson(await js.InvokeAsync<string?>("localStorage.getItem", DesignKey));

    /// <summary>
    /// Saves a card design and makes its template the table-card template. Throws <see cref="JSException"/> when
    /// browser storage is full.
    /// </summary>
    public async Task SaveDesignAsync(CardDesign design)
    {
        await js.InvokeVoidAsync("localStorage.setItem", DesignKey, design.ToJson());
        await SaveAsync(PrintKind.TableCards, design.ToTemplate());
    }
}
