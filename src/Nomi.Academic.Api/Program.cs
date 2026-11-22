using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Nomi.Academic.Infrastructure;
using Nomi.Academic.Api;
using Nomi.Academic.Infrastructure.Persistence;

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
// Without a database the process must still start (liveness 200, readiness 503).
// Mapping the feature routes would fail parameter inference for AcademicDbContext
// on every request, so v1 reports the missing dependency instead.
if (app.Services.GetRequiredService<IServiceProviderIsService>().IsService(typeof(AcademicDbContext)))
{
    app.MapSubjectEndpoints();
    app.MapProfessorEndpoints();
    app.MapProfileEndpoints();
    app.MapAgendaEndpoints();
    app.MapGroupsEndpoints();
}
else
{
    app.Map("/api/v1/{**path}", () => Results.StatusCode(StatusCodes.Status503ServiceUnavailable));
}
app.Run();

public partial class Program;
