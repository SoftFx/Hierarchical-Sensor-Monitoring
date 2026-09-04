using System.Collections.Generic;
using System.Linq;
using HSMServer.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace HSMServer.Model.ManagementApi
{
    // The `error` codes of the uniform error contract (see ManagementApiErrorDto) and
    // the factory of its controller-side results. The codes are the machine-facing API
    // of the area — agents switch on them — so they map 1:1 onto HTTP status codes and
    // are append-only: a code is never renamed or reused for another status.
    public static class ManagementApiErrors
    {
        // 400 — request-shape or validation failure; details carries field-keyed messages.
        public const string ValidationFailedCode = "validation_failed";

        // 401 — missing or invalid bearer credential.
        public const string UnauthorizedCode = "unauthorized";

        // 403 — authenticated, but the token's grants do not cover the operation.
        public const string ForbiddenCode = "forbidden";

        // 404 — unknown, invisible or out-of-reach resource.
        public const string NotFoundCode = "not_found";

        // 409 — the write conflicts with server-side state.
        public const string ConflictCode = "conflict";

        // 500 — unhandled server error; details carries {traceId}.
        public const string InternalErrorCode = "internal_error";


        // The single 404 message of the area: area-guard rejections (no such route,
        // wrong port, non-conforming endpoint), unknown ids and the authorization
        // evaluator's invisible-folder decision all render the SAME body — nothing
        // about resource existence or reachability may leak from it.
        public const string NotFoundMessage = "The requested resource was not found.";


        public static ObjectResult NotFound() =>
            new(new ManagementApiErrorDto { Error = NotFoundCode, Message = NotFoundMessage })
            {
                StatusCode = StatusCodes.Status404NotFound,
            };

        public static ObjectResult Forbidden(string message) =>
            new(new ManagementApiErrorDto { Error = ForbiddenCode, Message = message })
            {
                StatusCode = StatusCodes.Status403Forbidden,
            };

        public static ObjectResult Conflict(string message) =>
            new(new ManagementApiErrorDto { Error = ConflictCode, Message = message })
            {
                StatusCode = StatusCodes.Status409Conflict,
            };

        // 400 with field-keyed details; an empty error map is not a validation failure.
        public static ObjectResult Validation(IDictionary<string, string[]> errors) =>
            new(new ManagementApiErrorDto
            {
                Error = ValidationFailedCode,
                Message = "The request is invalid.",
                Details = errors is { Count: > 0 } ? errors : null,
            })
            {
                StatusCode = StatusCodes.Status400BadRequest,
            };

        // The [ApiController] automatic-400 route into the contract (wired as
        // ApiBehaviorOptions.InvalidModelStateResponseFactory): binding failures —
        // malformed JSON, wrong types — bypass the action body, so they cannot go
        // through the controllers' Validation calls. Scoped to management actions by
        // the controller marker; every other ApiController keeps the framework's
        // ValidationProblemDetails shape.
        public static IActionResult BindingFailureResponse(ActionContext context) =>
            IsManagementAction(context)
                ? Validation(FromModelState(context.ModelState))
                : new BadRequestObjectResult(new ValidationProblemDetails(context.ModelState));

        // The area-membership check of the binding-failure factory: the controller
        // carries [ManagementApi] — the same marker the area guard admits endpoints by.
        private static bool IsManagementAction(ActionContext context) =>
            context.ActionDescriptor is ControllerActionDescriptor action &&
            action.ControllerTypeInfo.IsDefined(typeof(ManagementApiAttribute), inherit: false);

        // ModelState -> the contract's field-keyed details map. Keys stay exactly what
        // MVC produced (property paths, $.path[0] JSON paths) so a client can locate
        // the offending entry, same as with the controllers' manual validation.
        public static IDictionary<string, string[]> FromModelState(ModelStateDictionary modelState) =>
            modelState
                .Where(entry => entry.Value.Errors.Count > 0)
                .ToDictionary(
                    entry => entry.Key,
                    entry => entry.Value.Errors.Select(error => error.ErrorMessage).ToArray());
    }
}
