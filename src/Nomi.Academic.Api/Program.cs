using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Nomi.Academic.Infrastructure;
using Nomi.Academic.Api;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddProblemDetails();
builder.Services.AddAcademicPersistence(builder.Configuration);

var app = builder.Build();
app.UseExceptionHandler();
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready")
});
app.MapSubjectEndpoints();
app.MapProfessorEndpoints();
app.MapProfileEndpoints();
app.MapAgendaEndpoints();
app.MapGroupsEndpoints();
app.Run();

public partial class Program;
