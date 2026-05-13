using Microsoft.OpenApi;
using RestLib;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

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

var app = builder.Build();

app.MapOpenApi();
app.MapScalarApiReference("/", options =>
{
    options.WithTitle("RestLib Ecommerce Sample API")
        .WithDefaultHttpClient(ScalarTarget.CSharp, ScalarClient.HttpClient);
});

app.MapHealthChecks("/health");

app.Run();
