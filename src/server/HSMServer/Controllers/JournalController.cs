using System;
using System.Linq;
using System.Threading.Tasks;
using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Authentication;
using HSMServer.Core.Journal;
using HSMServer.Extensions;
using HSMServer.Model.ViewModel;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HSMServer.Controllers;

[Authorize]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class JournalController : BaseController
{
    private readonly IJournalService _journalService;
    
    public JournalController(IUserManager userManager, IJournalService journalService) : base(userManager)
    {
        _journalService = journalService;
    }
    
    public async Task<IActionResult> GetJournalsPage(string id)
    {
        var journals = (await _journalService.GetJournalValuesPage(Guid.Parse(id), DateTime.MinValue, DateTime.MaxValue, RecordType.Actions, -100)
            .Flatten()).Select(x => new JournalViewModel(x)).ToList();
        return PartialView("_JournalsTable", journals);
    }
}