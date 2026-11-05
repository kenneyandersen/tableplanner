using Microsoft.JSInterop;
using TablePlanner.Export.Printing;

namespace TablePlanner.Web.Services;

/// <summary>
/// Keeps the user's print templates (a <see cref="TemplateLibrary"/>) in the browser's localStorage, separately from
/// projects, so they are reused for every event.
/// </summary>
public sealed class TemplateStore(IJSRuntime js)
{
    private const string LibraryKey = "tableplanner:templatelibrary";

    // What earlier versions kept: one own template per kind and one card design.
    private static string EarlierKey(PrintKind kind) => $"tableplanner:template:{kind.ToString().ToLowerInvariant()}";
    private const string EarlierDesignKey = "tableplanner:carddesign";

    public async Task<TemplateLibrary> LoadAsync()
    {
        if (TemplateLibrary.FromJson(await js.InvokeAsync<string?>("localStorage.getItem", LibraryKey)) is { } library)
            return library;

        var own = new Dictionary<PrintKind, string?>();
        foreach (var kind in Enum.GetValues<PrintKind>())
            own[kind] = await js.InvokeAsync<string?>("localStorage.getItem", EarlierKey(kind));
        var design = await js.InvokeAsync<string?>("localStorage.getItem", EarlierDesignKey);
        library = TemplateLibrary.FromEarlierVersion(own, design, DateTimeOffset.Now);
        if (library.Templates.Count > 0)
        {
            await SaveAsync(library);
            foreach (var kind in Enum.GetValues<PrintKind>())
                await js.InvokeVoidAsync("localStorage.removeItem", EarlierKey(kind));
            await js.InvokeVoidAsync("localStorage.removeItem", EarlierDesignKey);
        }
        return library;
    }

    /// <summary>Throws <see cref="JSException"/> when browser storage is full.</summary>
    public Task SaveAsync(TemplateLibrary library) => js.InvokeVoidAsync("localStorage.setItem", LibraryKey, library.ToJson()).AsTask();
}
