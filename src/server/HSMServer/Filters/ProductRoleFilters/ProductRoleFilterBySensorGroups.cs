using HSMServer.Model.Authentication;
using HSMServer.Model.ViewModel;
using Microsoft.AspNetCore.Mvc.Filters;
using System;

namespace HSMServer.Filters.ProductRoleFilters
{
    /// <summary>
    /// Authorizes <see cref="SensorGroupsRequest"/>-bodied actions (toggling a product's sensor
    /// groups, #1198) against the role the user holds on the request's target product. Without this,
    /// any authenticated user could disable data collection on any product by POSTing its id.
    /// </summary>
    public sealed class ProductRoleFilterBySensorGroups : ProductRoleFilterBase
    {
        public ProductRoleFilterBySensorGroups(string argumentName, params ProductRoleEnum[] roles) : base(argumentName, roles) { }


        protected override Guid? GetEntityId(object arg, ActionExecutingContext _) =>
            arg is SensorGroupsRequest request ? request.ProductId : null;
    }
}
