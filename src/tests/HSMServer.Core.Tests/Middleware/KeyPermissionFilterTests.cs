using System;
using System.Threading.Tasks;
using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Core.Model;
using HSMServer.Middleware;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Xunit;

namespace HSMServer.Core.Tests.Middleware
{
    // #1649: the key-permission filter used to answer a key without the
    // needed permission by writing the 406 body straight to the response —
    // the response started, and whatever MVC ran next (the action's own
    // result formatting) threw InvalidOperationException over it: three
    // ERROR entries and a truncated body per rejected request. The 406 now
    // rides context.Result — the response never starts in the filter, and
    // MVC formats the rejection itself.
    public sealed class KeyPermissionFilterTests
    {
        [Fact]
        public async Task KeyWithoutPermission_Answers406ThroughTheResult()
        {
            var context = Context(KeyPermissions.CanReadSensorData);
            var filter = new SendDataKeyPermissionFilter();

            await filter.OnResultExecutionAsync(context, Next(context));

            var rejection = Assert.IsType<ContentResult>(context.Result);
            Assert.Equal(406, rejection.StatusCode);
            Assert.Contains("AccessKey doesn't have", rejection.Content, StringComparison.Ordinal);
            Assert.False(context.HttpContext.Response.HasStarted);
        }

        [Fact]
        public async Task ValidKey_ResultUntouched()
        {
            var original = new OkResult();
            var context = Context(KeyPermissions.CanSendSensorData | KeyPermissions.CanReadSensorData);
            context.Result = original;
            var filter = new SendDataKeyPermissionFilter();

            await filter.OnResultExecutionAsync(context, Next(context));

            Assert.Same(original, context.Result);
            Assert.False(context.HttpContext.Response.HasStarted);
        }


        private static ResultExecutionDelegate Next(ResultExecutingContext context) =>
            () => Task.FromResult(new ResultExecutedContext(
                context, new IFilterMetadata[0], context.Result, new object()));

        private static ResultExecutingContext Context(KeyPermissions permissions)
        {
            var http = new DefaultHttpContext();
            http.Items[nameof(PublicApiRequestInfo)] = new PublicApiRequestInfo { Key = KeyOf(permissions) };

            return new ResultExecutingContext(
                new ActionContext(http, new RouteData(), new Microsoft.AspNetCore.Mvc.Abstractions.ActionDescriptor()),
                new IFilterMetadata[0],
                new OkResult(),
                new object());
        }

        // A live key built through the storage constructor: state Active,
        // expiration far away — the state checks pass and the permissions
        // decide.
        private static AccessKeyModel KeyOf(KeyPermissions permissions) => new(new AccessKeyEntity
        {
            Id = Guid.NewGuid().ToString(),
            ProductId = Guid.NewGuid().ToString(),
            State = (byte)KeyState.Active,
            Permissions = (long)permissions,
            CreationTime = DateTime.UtcNow.Ticks,
            ExpirationTime = DateTime.MaxValue.Ticks,
        });
    }
}
