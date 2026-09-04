using System;
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

        // The [ApiController] automatic-400 route into the contract for MANAGEMENT
        // actions: binding failures — malformed JSON, wrong types — bypass the action
        // body, so they cannot go through the controllers' Validation calls.
        public static IActionResult BindingFailureResponse(ActionContext context) =>
            Validation(FromModelState(context.ModelState));

        // The composition wired in Program.cs as InvalidModelStateResponseFactory:
        // management actions get the uniform contract, everything else goes to the
        // CAPTURED framework default verbatim — the sensor-data and Grafana APIs keep
        // their exact previous wire shape (problem+json content types, type, traceId),
        // not a reimplementation of it.
        public static Func<ActionContext, IActionResult> WrapBindingFailureFactory(
            Func<ActionContext, IActionResult> frameworkDefault) =>
            context => IsManagementAction(context)
                ? BindingFailureResponse(context)
                : frameworkDefault(context);

        // The area-membership check of the binding-failure factory: the controller
        // carries [ManagementApi] — the same marker the area guard admits endpoints
        // by. inherit: true — endpoint metadata (what the guard reads) includes
        // attributes inherited from a base controller, and this check must not
        // silently diverge from it.
        public static bool IsManagementAction(ActionContext context) =>
            context.ActionDescriptor is ControllerActionDescriptor action &&
            action.ControllerTypeInfo.IsDefined(typeof(ManagementApiAttribute), inherit: true);

        // ModelState -> the contract's field-keyed details map. Keys stay exactly what
        // MVC produced (property paths, $.path[0] JSON paths) so a client can locate
        // the offending entry, same as with the controllers' manual validation. An
        // empty error message (a binder exception that is not a Format/
        // Overflow/InputFormatter one) gets the framework's own fallback wording —
        // a field key with an empty message is not actionable.
        public static IDictionary<string, string[]> FromModelState(ModelStateDictionary modelState) =>
            modelState
                .Where(entry => entry.Value.Errors.Count > 0)
                .ToDictionary(
                    entry => entry.Key,
                    entry => entry.Value.Errors
                        .Select(error => string.IsNullOrEmpty(error.ErrorMessage)
                            ? "The input was not valid."
                            : error.ErrorMessage)
                        .ToArray());
    }
}
