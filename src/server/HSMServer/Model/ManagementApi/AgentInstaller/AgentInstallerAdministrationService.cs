using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Claims;
using System.Security.Cryptography;
using HSMServer.Authentication;
using HSMServer.Core.Cache;
using HSMServer.Core.Model;
using HSMServer.Model.Agent;
using HSMServer.Model.Authentication;
using HSMServer.ServerConfiguration;
using Microsoft.AspNetCore.Connections.Features;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;

namespace HSMServer.Model.ManagementApi.AgentInstaller
{
    /// <summary>
    /// The request facts the bundle build and the link URL need — captured
    /// once per call (REST controller or MCP tool) through <see cref="Of"/>.
    /// </summary>
    // TWO host forms, deliberately: the resolver's contract is host WITHOUT
    // port (the port travels separately), while the link URL's authority
    // carries the port when non-default — conflating them bakes a malformed
    // address into the agent's config on the no-ExternalConnectionUrl
    // fallback (#1796 review).
    public sealed record InstallerRequestContext
    {
        public ClaimsPrincipal User { get; init; }

        public string Scheme { get; init; }

        /// <summary>The request host WITHOUT port (the resolver's input).</summary>
        public string Host { get; init; }

        public int? HostPort { get; init; }

        /// <summary>The request host WITH port when non-default (the link URL's authority).</summary>
        public string UrlAuthority { get; init; }

        public bool TlsHandshake { get; init; }

        public bool ForwardedByOriginalFor { get; init; }


        public static InstallerRequestContext Of(ClaimsPrincipal user, HttpContext http) => new()
        {
            User = user,
            Scheme = http.Request.Scheme,
            Host = http.Request.Host.Host,
            HostPort = http.Request.Host.Port,
            UrlAuthority = http.Request.Host.ToString(),
            TlsHandshake = http.Features.Get<ITlsHandshakeFeature>() is not null,
            ForwardedByOriginalFor = http.Request.Headers.ContainsKey(ForwardedHeadersDefaults.XOriginalForHeaderName),
        };
    }

    /// <summary>
    /// The agent-installer surface's engine (#1788): admission at the product
    /// (the owner's ProductManager role — a read-only token suffices, the
    /// initiative's decision #5: the bundle discloses a credential the PM can
    /// already read), the bundle build (the SAME builders the web Downloads
    /// page drives, embedding the product's existing send-data key — never
    /// generating one), and the one-time link issuance for MCP-native
    /// orchestrators. Transport-agnostic: the REST controller and the MCP
    /// tool render its results through the shared envelopes.
    /// </summary>
    // ADMISSION COMPOSITION (the CanSeeChatDetail pattern): the evaluator's
    // AuthorizeRead owns the 404/403 anti-enumeration split (unknown and
    // invisible products answer the same 404); the ProductManager half is an
    // owner-side role check resolved directly from the user manager — because
    // AuthorizeWrite would also reject READ-ONLY tokens, and decision #5
    // keeps them able to fetch the bundle (they can already read the key GUID
    // through the access-keys surface; the bundle adds nothing new to their
    // sight level). Admins pass, as everywhere.
    //
    // The bundle BUILD is the AgentController.Installer/LinuxInstaller flow
    // extracted (staging checks, key selection, address resolution from the
    // CALLER'S request, the Linux server-CA decision). One content difference
    // from the web flow, deliberate: the README's link to the web Downloads
    // page is web-flow-only (an API orchestrator has no web session; the
    // README keeps its generic hint) — so the bundle CONTENTS follow the same
    // contract (signed exe byte-identical, generated config, install
    // scripts) without being byte-identical to the web page's zip.
    //
    // The read envelope (ManagementReadResult) carries the surface's whole
    // outcome set — including the Unavailable arm a not-staged binary needs
    // (a capacity abort, not a conflict: the remedy is publishing the
    // binary, and a 409 would invite a caller-side "retry differently" that
    // cannot help).
    //
    // The link carries the BUILT bytes (see AgentInstallerDownloadLinks):
    // size/sha256 are computed once at issue time and the redemption streams
    // byte-identical content. The URL is host-absolute from the CALLER'S
    // request — never the agent ExternalConnectionUrl (that is the data
    // channel, not this API).
    public sealed class AgentInstallerAdministrationService
    {
        internal const string WindowsOs = "windows";
        internal const string LinuxOs = "linux";

        internal const string NoKeyMessage =
            "This product has no usable access key. Create one with send-data permission first.";

        private static readonly NLog.Logger _logger = NLog.LogManager.GetCurrentClassLogger();

        private readonly ITreeValuesCache _cache;
        private readonly IApiTokenAuthorizationService _authorization;
        private readonly IUserManager _users;
        private readonly IServerConfig _config;
        private readonly IWebHostEnvironment _environment;
        private readonly AgentInstallerDownloadLinks _links;

        public AgentInstallerAdministrationService(ITreeValuesCache cache,
            IApiTokenAuthorizationService authorization, IUserManager users, IServerConfig config,
            IWebHostEnvironment environment, AgentInstallerDownloadLinks links)
        {
            _cache = cache;
            _authorization = authorization;
            _users = users;
            _config = config;
            _environment = environment;
            _links = links;
        }


        /// <summary>
        /// Build the product's bundle and answer it for direct streaming
        /// (the script path — the management bearer on every request).
        /// </summary>
        public ManagementReadResult<AgentInstallerBundleResult> GetBundle(Guid productId, string os,
            InstallerRequestContext request)
        {
            var admission = Admit(productId, request.User, os);

            if (admission.Failure is not null)
                return ManagementReadResult<AgentInstallerBundleResult>.FromFailure(admission.Failure);

            var build = Build(admission.Product, os, request);

            return build.Failure is not null
                ? ManagementReadResult<AgentInstallerBundleResult>.FromFailure(build.Failure)
                : ManagementReadResult<AgentInstallerBundleResult>.Ok(build.Bundle);
        }

        /// <summary>
        /// Build the product's bundle and issue a ONE-TIME short-lived
        /// download link for it — the MCP-native hand-over: the orchestrator
        /// curls the link on the target machine without the management
        /// bearer. The result carries the absolute URL, the expiry, the file
        /// name, the size and the sha256 of the EXACT bytes the redemption
        /// will serve.
        /// </summary>
        public ManagementReadResult<AgentInstallerLinkDto> IssueLink(Guid productId, string os,
            InstallerRequestContext request)
        {
            var admission = Admit(productId, request.User, os);

            if (admission.Failure is not null)
                return ManagementReadResult<AgentInstallerLinkDto>.FromFailure(admission.Failure);

            var build = Build(admission.Product, os, request);

            if (build.Failure is not null)
                return ManagementReadResult<AgentInstallerLinkDto>.FromFailure(build.Failure);

            var (token, expiresAt) = _links.Issue(new AgentInstallerDownloadLinks.Link(
                build.Bundle.FileName, build.Bundle.ContentType, build.Bundle.Bundle));

            return ManagementReadResult<AgentInstallerLinkDto>.Ok(new AgentInstallerLinkDto
            {
                // Host-absolute from the CALLER'S request — the management
                // API's own host, never the agent data-channel URL.
                Url = $"{request.Scheme}://{request.UrlAuthority}/api/agent/installer-download?token={token}",
                ExpiresAt = expiresAt,
                FileName = build.Bundle.FileName,
                Size = build.Bundle.Bundle.LongLength,
                Sha256 = Sha256Hex(build.Bundle.Bundle),
            });
        }


        // Admission: the evaluator's read decision (unknown and invisible →
        // the same 404), then the owner-side ProductManager check (admins
        // pass). The os parameter validates first — it is the caller's own
        // mistake, not a state question.
        private (ProductModel Product, ManagementReadFailure Failure) Admit(Guid productId,
            ClaimsPrincipal user, string os)
        {
            if (os is not (WindowsOs or LinuxOs))
            {
                return (null, new ManagementReadFailure
                {
                    Outcome = ManagementReadOutcome.ValidationFailed,
                    Errors = new Dictionary<string, string[]>
                    {
                        ["os"] = [$"The os must be '{WindowsOs}' or '{LinuxOs}' (got '{os}')."],
                    },
                });
            }

            if (!_cache.TryGetProduct(productId, out var product) || product is null)
                return (null, new ManagementReadFailure { Outcome = ManagementReadOutcome.NotFound });

            if (_authorization.AuthorizeRead(user, ApiTokenResource.Product(product.Root.Id)) != ApiTokenAuthorization.Allowed)
                return (null, new ManagementReadFailure { Outcome = ManagementReadOutcome.NotFound });

            // The PM half of decision #5, resolved owner-side: AuthorizeWrite
            // would reject read-only tokens, which this surface keeps.
            var owner = OwnerOf(user);

            if (owner?.IsAdmin != true && owner?.IsManager(product.Root.Id) != true)
            {
                return (null, new ManagementReadFailure
                {
                    Outcome = ManagementReadOutcome.Forbidden,
                    Message = "The token's owner must hold the ProductManager role at this product.",
                });
            }

            return (product, null);
        }

        // The AgentController build flow: key selection (no usable send-data
        // key is the caller-actionable 400 the ticket names), staging checks
        // (503), address resolution from the CALLER'S request, and — on the
        // Linux path — the server-CA decision. The outcome carries either the
        // built bundle or a field-keyed failure.
        private (AgentInstallerBundleResult Bundle, ManagementReadFailure Failure) Build(
            ProductModel product, string os, InstallerRequestContext request)
        {
            var key = AgentKeySelector.Select(product);

            if (key is null)
            {
                return (null, new ManagementReadFailure
                {
                    Outcome = ManagementReadOutcome.ValidationFailed,
                    Errors = new Dictionary<string, string[]> { ["accessKey"] = [NoKeyMessage] },
                });
            }

            if (_environment.WebRootPath is not { Length: > 0 })
                return (null, NotStaged(AgentInstallerBundle.NotStagedMessage));

            // The resolver's contract: host WITHOUT port (the port travels
            // separately) — the AgentController twin passes Request.Host.Host
            // the same way.
            var (address, port) = AgentConnectionResolver.Resolve(
                _config.Agent.ExternalConnectionUrl,
                AgentConnectionResolver.FallbackPortFromHost(request.HostPort, _config.Kestrel.Port),
                request.Scheme, request.Host);

            if (os == WindowsOs)
            {
                var zip = BuildWindows(product, key, address, port);

                return zip is null
                    ? (null, NotStaged(AgentInstallerBundle.NotStagedMessage))
                    : (zip, null);
            }

            return BuildLinux(product, key, address, port, request);
        }

        private AgentInstallerBundleResult BuildWindows(ProductModel product, AccessKeyModel key,
            string address, int port)
        {
            byte[] exeBytes;

            try
            {
                exeBytes = File.ReadAllBytes(Path.Combine(_environment.WebRootPath,
                    AgentInstallerBundle.StagingFolder, AgentInstallerBundle.ExeName));
            }
            catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException or IOException)
            {
                return null;
            }

            // The README's downloads-page link is web-flow-only: this surface
            // has no web session, and the README keeps its generic hint.
            var options = new AgentBundleOptions(address, port, key.Id.ToString(),
                _config.Agent.AllowUntrustedCertificate, _config.Agent.EnableTopCpuProcesses);

            var zip = AgentInstallerBundle.BuildZip(exeBytes, options);

            return new AgentInstallerBundleResult(zip,
                $"hsm-agent-{LinuxProbeInstallerBundle.ShellSafeName(product.DisplayName)}.zip",
                "application/zip");
        }

        private (AgentInstallerBundleResult Bundle, ManagementReadFailure Failure) BuildLinux(
            ProductModel product, AccessKeyModel key, string address, int port,
            InstallerRequestContext request)
        {
            var stagingDir = Path.Combine(_environment.WebRootPath, LinuxProbeInstallerBundle.StagingFolder);

            string packageName;

            try
            {
                packageName = LinuxProbeInstallerBundle.SelectStagedPackage(Directory.EnumerateFiles(stagingDir));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                packageName = null;
            }

            if (packageName is null)
                return (null, NotStaged(LinuxProbeInstallerBundle.NotStagedMessage));

            var addressError = LinuxProbeInstallerBundle.ValidateServerAddress(address);

            if (addressError is not null)
            {
                return (null, new ManagementReadFailure
                {
                    Outcome = ManagementReadOutcome.ValidationFailed,
                    Errors = new Dictionary<string, string[]> { ["address"] = [addressError] },
                });
            }

            // Whose certificate the probe verifies (the AgentController
            // comment): this server's only when Kestrel terminated the TLS
            // the client saw, not a trusted proxy's.
            var serverTerminatesTls = LinuxProbeServerCa.ServerTerminatesClientTls(
                request.TlsHandshake, _config.Kestrel.TrustedProxies.Length > 0, request.ForwardedByOriginalFor);

            var (caDecision, serverCa) = LinuxProbeServerCa.Resolve(serverTerminatesTls,
                () => (_config.ServerCertificate.Certificate, _config.ServerCertificate.IsBundledDefault));

            if (caDecision == LinuxProbeCaDecision.RefuseBundledDefault)
            {
                return (null, new ManagementReadFailure
                {
                    Outcome = ManagementReadOutcome.ValidationFailed,
                    Errors = new Dictionary<string, string[]> { ["certificate"] = [LinuxProbeServerCa.BundledDefaultMessage] },
                });
            }

            if (caDecision == LinuxProbeCaDecision.OmitUnreadable)
                _logger.Warn($"Linux probe bundle for product '{product.DisplayName}' ({product.Id}) ships without server-ca.pem: the server certificate could not be read, so a probe will only connect if that certificate is already trusted on its host.");

            byte[] package;

            try
            {
                package = File.ReadAllBytes(Path.Combine(stagingDir, packageName));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return (null, NotStaged(LinuxProbeInstallerBundle.NotStagedMessage));
            }

            var options = new LinuxProbeBundleOptions(address, port, key.Id.ToString(),
                serverCa, _config.Agent.EnableTopCpuProcesses);

            var bundle = LinuxProbeInstallerBundle.BuildTarGz(
                LinuxProbeInstallerBundle.BundleFolderName(product.DisplayName), packageName, package, options);

            return (new AgentInstallerBundleResult(bundle,
                LinuxProbeInstallerBundle.BundleFileName(product.DisplayName), "application/gzip"), null);
        }

        // Not-staged is a capacity abort the caller cannot fix by changing
        // the request — the area's service_unavailable (#1387 r4), with the
        // remedy (publish the binary) in the message.
        private static ManagementReadFailure NotStaged(string message) =>
            new() { Outcome = ManagementReadOutcome.Unavailable, Message = message };

        private static string Sha256Hex(byte[] bytes)
        {
            var hash = SHA256.HashData(bytes);

            return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
        }

        private User OwnerOf(ClaimsPrincipal user)
        {
            var ownerClaim = user?.FindFirst(HsmApiTokenClaims.OwnerUserId)?.Value;

            return Guid.TryParse(ownerClaim, out var ownerId) ? _users[ownerId] : null;
        }


        /// <summary>The built bundle: the bytes, the download file name, the content type.</summary>
        public sealed record AgentInstallerBundleResult(byte[] Bundle, string FileName, string ContentType);
    }
}
