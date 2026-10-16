using System.Text.Json;
using FluentValidation;
using HabitFlow.Application.Common;
using HabitFlow.Domain.Common;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace HabitFlow.Api.Web;

/// <summary>
/// Maps exceptions to RFC 9457 problem responses with a stable <c>code</c> extension. Unexpected errors are logged
/// and answered with a generic message: stack traces and internal details never reach the client.
/// </summary>
internal sealed class GlobalExceptionHandler(
    IProblemDetailsService problemDetailsService,
    ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken ct)
    {
        if (exception is OperationCanceledException && httpContext.RequestAborted.IsCancellationRequested)
        {
            // The client went away; nothing useful to answer.
            return true;
        }

        var problem = exception switch
        {
            ValidationException validation => CreateValidationProblem(validation),
            NotFoundException notFound => Problem(StatusCodes.Status404NotFound, "not_found", notFound.Message),
            ConflictException conflict => Problem(StatusCodes.Status409Conflict, conflict.Code, conflict.Message),
            DomainException domain => Problem(StatusCodes.Status422UnprocessableEntity, domain.Code, domain.Message),
            FeatureUnavailableException unavailable =>
                Problem(StatusCodes.Status503ServiceUnavailable, unavailable.Code, unavailable.Message),
            AuthenticationFailedException auth => Problem(StatusCodes.Status401Unauthorized, "unauthorized", auth.Message),
            AntiforgeryValidationException => Problem(StatusCodes.Status403Forbidden, "csrf_invalid",
                "La sesión de seguridad del formulario caducó. Recarga la página e inténtalo de nuevo."),
            BadHttpRequestException { StatusCode: StatusCodes.Status413PayloadTooLarge } => Problem(
                StatusCodes.Status413PayloadTooLarge, "payload_too_large", "La solicitud supera el tamaño máximo permitido."),
            BadHttpRequestException badRequest => Problem(badRequest.StatusCode, "bad_request", "La solicitud no es válida."),
            _ => null,
        };

        if (problem is null)
        {
            logger.LogError(exception, "Unhandled exception for {Method} {Path}", httpContext.Request.Method,
                httpContext.Request.Path);
            problem = Problem(StatusCodes.Status500InternalServerError, "internal_error",
                "Ha ocurrido un error inesperado. Inténtalo de nuevo más tarde.");
        }

        httpContext.Response.StatusCode = problem.Status ?? StatusCodes.Status500InternalServerError;
        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = problem,
            Exception = exception,
        });
    }

    internal static ProblemDetails Problem(int status, string code, string detail) => new()
    {
        Status = status,
        Title = TitleFor(status),
        Detail = detail,
        Extensions = { ["code"] = code },
    };

    private static ValidationProblemDetails CreateValidationProblem(ValidationException exception)
    {
        var errors = exception.Errors
            .GroupBy(e => CamelCasePath(e.PropertyName))
            .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).Distinct().ToArray());

        return new ValidationProblemDetails(errors)
        {
            Status = StatusCodes.Status400BadRequest,
            Title = TitleFor(StatusCodes.Status400BadRequest),
            Detail = "Revisa los datos enviados.",
            Extensions = { ["code"] = "validation_failed" },
        };
    }

    /// <summary>"Schedule.TimesPerWeek" → "schedule.timesPerWeek", matching the JSON the client sent.</summary>
    private static string CamelCasePath(string path) =>
        string.Join('.', path.Split('.').Select(JsonNamingPolicy.CamelCase.ConvertName));

    private static string TitleFor(int status) => status switch
    {
        400 => "Solicitud no válida",
        401 => "No autenticado",
        403 => "Acceso denegado",
        404 => "No encontrado",
        409 => "Conflicto",
        413 => "Contenido demasiado grande",
        422 => "Regla de negocio incumplida",
        429 => "Demasiadas solicitudes",
        503 => "Función no disponible",
        _ => "Error del servidor",
    };
}
