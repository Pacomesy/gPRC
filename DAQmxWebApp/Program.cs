using DAQmxWebApp.Services;
using DAQmxWebApp.Components;
using Microsoft.AspNetCore.DataProtection;

// gRPC over http:// requires HTTP/2 without TLS (.NET default blocks h2c until this is set)
AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);

var builder = WebApplication.CreateBuilder(args);

// Persist Data Protection keys to a stable directory (mounted as a Docker volume).
// Using a fixed application name ensures keys survive container restarts.
builder.Services.AddDataProtection()
    .SetApplicationName("daqmx-webapp")
    .PersistKeysToFileSystem(new DirectoryInfo("/root/.aspnet/DataProtection-Keys"));

// Use a fixed cookie name so the browser never presents a stale cookie
// encrypted with keys from a previous container instance.
builder.Services.AddAntiforgery(options =>
{
    options.Cookie.Name = "DAQmx-AF";
});

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddSingleton<DAQmxService>();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
}

app.UseStaticFiles();
app.UseAntiforgery();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
