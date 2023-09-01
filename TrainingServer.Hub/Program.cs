using TrainingServer.Hub.Data;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorPages();
builder.Services.AddServerSideBlazor();
builder.Services.AddSingleton<ConnectionManager>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
	app.UseExceptionHandler("/Error");
}


app.UseStaticFiles();

app.UseRouting();
app.UseWebSockets();

app.MapBlazorHub();
app.MapFallbackToPage("/_Host");

app.Use(async (context, next) =>
{
	if (context.Request.Path.StartsWithSegments("/connect") && context.WebSockets.IsWebSocketRequest)
	{
		var manager = app.Services.GetRequiredService<ConnectionManager>();

		if (context.Request.Path.ToString().TrimEnd('/') == "/connect")
			await manager.AcceptServerAsync(await context.WebSockets.AcceptWebSocketAsync(new WebSocketAcceptContext() { DangerousEnableCompression = true }));
		else
		{
			string server = context.Request.Path.Value!["/connect/".Length..].TrimEnd('/');
			await manager.AcceptClientAsync(server, await context.WebSockets.AcceptWebSocketAsync(new WebSocketAcceptContext() { DangerousEnableCompression = true }));
		}
	}
	else
		await next(context);
});

app.Run();
