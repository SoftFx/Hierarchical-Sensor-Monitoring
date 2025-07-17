using HSMServer.Authentication;
using HSMServer.Core.TableOfChanges;
using HSMServer.Model.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System;

namespace HSMServer.Controllers
{
    [Authorize]
    public abstract class BaseController : Controller
    {
        protected static readonly JsonResult _emptyJsonResult = new(new EmptyResult());
        protected static readonly EmptyResult _emptyResult = new();

        protected readonly IUserManager _userManager;


        public InitiatorInfo CurrentInitiator => InitiatorInfo.AsUser(CurrentUser.Name);

        public User CurrentUser => HttpContext.User switch
        {
            User user => user,
            null => throw new UnauthorizedAccessException("User is not authenticated"),
            _ => throw new InvalidCastException($"Expected User type, got {HttpContext.User.GetType()}")
        };

        public User StoredUser => _userManager[CurrentUser.Id];


        protected BaseController(IUserManager userManager)
        {
            _userManager = userManager;
        }
    }
}
