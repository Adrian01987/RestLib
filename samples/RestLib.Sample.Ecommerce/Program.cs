using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi;
using RestLib;
using RestLib.Sample.Ecommerce.Data;
using RestLib.Sample.Ecommerce.Identity;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);
var useMigrations = builder.Configuration.GetValue<bool>("RestLibSample:UseMigrations");

builder.Services.AddRestLib(options =>
{
    options.EnableETagSupport = true;
    options.EnableHateoas = true;
    options.RequireAuthorizationByDefault = true;
});

builder.Services.AddOpenApi(options =>
{
    options.AddDocumentTransformer((document, context, ct) =>
    {
        document.Info = new OpenApiInfo
        {
            Title = "RestLib Ecommerce Sample API",
            Version = "v1",
            Description = "A reference ecommerce API demonstrating RestLib across core, InMemory, and EF Core adapters.",
            Contact = new OpenApiContact
            {
                Name = "RestLib",
                Url = new Uri("https://github.com/Adrian01987/RestLib"),
            },
        };

        return Task.CompletedTask;
    });
});

builder.Services.AddHealthChecks();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, HttpContextCurrentUser>();
builder.Services.AddDbContext<EcommerceDbContext>(options =>
{
    var connectionString = builder.Configuration.GetConnectionString("Ecommerce")
        ?? "Data Source=restlib-ecommerce.db";
    options.UseSqlite(connectionString);
});

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<EcommerceDbContext>();

    if (useMigrations)
    {
        await db.Database.MigrateAsync();
    }
    else
    {
        await db.Database.EnsureCreatedAsync();
    }

    await EcommerceSeedData.EnsureSeededAsync(db);
}

app.MapOpenApi();
app.MapScalarApiReference("/", options =>
{
    options.WithTitle("RestLib Ecommerce Sample API")
        .WithDefaultHttpClient(ScalarTarget.CSharp, ScalarClient.HttpClient);
});

app.MapHealthChecks("/health");

app.Run();
