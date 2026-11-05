using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using MudBlazor.Services;
using TablePlanner.Web;
using TablePlanner.Web.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddMudServices();
builder.Services.AddScoped<ImageStore>();
builder.Services.AddScoped<ProjectStore>();
builder.Services.AddScoped<ProjectSession>();
builder.Services.AddScoped<TemplateStore>();

await builder.Build().RunAsync();
