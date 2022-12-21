using HSMServer.Core.Model;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace HSMServer.Pagination
{
    public interface ISensorValuesHistoryPagination
    {
        List<BaseValue> CurrentPage { get; }

        int CurrentPageIndex { get; }

        bool HasNextPage { get; }

        bool HasPrevPage { get; }


        Task InitializeEnumerator(string encodedId, DateTime from, DateTime to);

        Task SwitchToNextPage();

        void SwitchToPrevPage();
    }
}
