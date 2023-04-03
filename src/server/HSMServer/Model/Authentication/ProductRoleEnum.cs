using System.ComponentModel.DataAnnotations;

namespace HSMServer.Model.Authentication
{
    public enum ProductRoleEnum
    {
        [Display(Name = "Manager")]
        ProductManager = 0,
        [Display(Name = "Viewer")]
        ProductViewer = 1
    }
}
