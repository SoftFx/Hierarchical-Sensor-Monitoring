using HSMWebClient.HtmlHelpers;
using HSMWebClient.Models;
using Microsoft.AspNetCore.Html;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace HSMWebClient.ApiControllers
{
    [ApiController]
    [Route("[controller]")]
    public class ViewApiController : Controller
    {

        [HttpGet]
        public string GetList(string url, int port, string path)
        {
            var result = ApiConnector.GetTree(url.Split(';')[0], port);

            TreeViewModel tree = new TreeViewModel(result);

            return ListHelper.CreateList(path, tree).ToString();
        }
    }
}
