using System;
using HSMServer.Authentication;
using HSMServer.Model.LargeFiles;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace HSMServer.Controllers
{
    public sealed record DumpPasswordRequest
    {
        public Guid ProductId { get; init; }
    }


    public sealed record DumpPasswordSetRequest
    {
        public Guid ProductId { get; init; }

        public string Password { get; init; }

        /// <summary>True when the field holds what "Generate" filled in, unchanged.</summary>
        public bool Generated { get; init; }
    }


    /// <summary>
    /// The dump password section of a product's settings page (ADR-0022 point 13, #1885): show the kept values, generate a
    /// password, save one. Manager of the ROOT product or admin only; POST + antiforgery; never cached. A value appears
    /// only in the JSON answer of these actions, never in a page and never in a log line.
    /// </summary>
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public sealed class DumpPasswordController : BaseController
    {
        private readonly DumpPasswordPageService _passwords;
        private readonly ILogger<DumpPasswordController> _logger;


        public DumpPasswordController(IUserManager userManager, DumpPasswordPageService passwords, ILogger<DumpPasswordController> logger) : base(userManager)
        {
            _passwords = passwords;
            _logger = logger;
        }


        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Show([FromBody] DumpPasswordRequest request) =>
            Render(_passwords.Show(CurrentUser, request?.ProductId ?? Guid.Empty), versions => new { versions });

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Generate([FromBody] DumpPasswordRequest request) =>
            Render(_passwords.Generate(CurrentUser, request?.ProductId ?? Guid.Empty), password => new { password });

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Set([FromBody] DumpPasswordSetRequest request)
        {
            var productId = request?.ProductId ?? Guid.Empty;
            var result = _passwords.Set(CurrentUser, productId, request?.Password, request?.Generated ?? false, CurrentInitiator);

            if (result.Outcome == DumpPasswordPageOutcome.Ok)
                _logger.LogInformation("Dump password of product {ProductId} set to version {Version} by '{User}'", productId, result.Value, CurrentUser.Name);

            return Render(result, version => new { version, message = result.Message });
        }


        private IActionResult Render<T>(DumpPasswordPageResult<T> result, Func<T, object> body) =>
            result.Outcome switch
            {
                DumpPasswordPageOutcome.Ok => new JsonResult(body(result.Value)),
                DumpPasswordPageOutcome.Invalid => new JsonResult(new { error = result.Message }) { StatusCode = StatusCodes.Status400BadRequest },
                DumpPasswordPageOutcome.Forbidden => StatusCode(StatusCodes.Status403Forbidden),
                _ => NotFound(),
            };
    }
}
