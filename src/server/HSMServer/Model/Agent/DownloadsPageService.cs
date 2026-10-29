using HSMServer.Model.Authentication;
using HSMServer.Model.Folders;
using HSMServer.ServerConfiguration;
using Microsoft.AspNetCore.Hosting;
using System;
using System.Linq;

namespace HSMServer.Model.Agent
{
    public interface IDownloadsPageService
    {
        /// <summary>
        /// The Downloads page for <paramref name="user"/>. <paramref name="requestScheme"/>,
        /// <paramref name="requestHost"/> and <paramref name="requestHostPort"/> are the fallback
        /// the bundles use when no agent connection URL is set.
        /// </summary>
        DownloadsViewModel Build(User user, string requestScheme, string requestHost, int? requestHostPort, Guid? selectedProductId = null);
    }


    /// <summary>
    /// Gathers the Downloads page's inputs (#1550): the root products the user sees in the tree (the same
    /// <see cref="TreeViewModel.TreeViewModel.GetUserProducts"/> the Products page lists), the web root the
    /// download endpoints read staged files from, and the connection address they bake into a bundle.
    /// </summary>
    public sealed class DownloadsPageService(TreeViewModel.TreeViewModel tree, IServerConfig config, IWebHostEnvironment environment) : IDownloadsPageService
    {
        public DownloadsViewModel Build(User user, string requestScheme, string requestHost, int? requestHostPort, Guid? selectedProductId = null)
        {
            var products = tree.GetUserProducts(user)
                .Select(p => new DownloadProductItem(p.Id, p.Name, (p.Parent as FolderModel)?.Name));

            var (address, port) = AgentConnectionResolver.Resolve(
                config.Agent.ExternalConnectionUrl, AgentConnectionResolver.FallbackPortFromHost(requestHostPort), requestScheme, requestHost);

            return DownloadsPageBuilder.Build(products, environment.WebRootPath, address, port, selectedProductId);
        }
    }
}
