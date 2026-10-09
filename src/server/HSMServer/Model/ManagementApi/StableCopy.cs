using System;
using System.Collections.Generic;
using System.Linq;

namespace HSMServer.Model.ManagementApi
{
    // Chat.Folders, FolderModel.Products and the DefaultChats SelectedChats
    // set are plain HashSet/Dictionary instances that web-UI requests mutate
    // in place WITHOUT a lock (ChatsManager.AddFolderToChats /
    // RemoveFolderFromChats, FolderManager's product add/remove/move,
    // DefaultChatViewModel.FromModel) — the owning managers expose no lock a
    // read path could take. Enumerating such a collection on a management
    // read path can throw InvalidOperationException mid-copy ("Collection
    // was modified"), which the area's JSON contract would otherwise render
    // as an HTML 500. The read side therefore retries the copy: the mutating
    // side's per-entity work is tiny, so a retry virtually always observes a
    // stable state. Deferred LINQ chains are fine — the whole chain
    // materializes inside one ToList, inside one attempt.
    internal static class StableCopy
    {
        private const int MaxAttempts = 3;

        public static List<T> Of<T>(Func<IEnumerable<T>> enumerate)
        {
            for (var attempt = 1; ; attempt++)
            {
                try
                {
                    return enumerate().ToList();
                }
                catch (InvalidOperationException) when (attempt < MaxAttempts)
                {
                }
            }
        }
    }
}
