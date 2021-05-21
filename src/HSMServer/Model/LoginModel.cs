using System.ComponentModel.DataAnnotations;
namespace HSMServer.Model
{
    public class LoginModel
    {
        [Required(ErrorMessage = "Enter login", AllowEmptyStrings = false)]
        public string Login { get; set; }
        [Required(ErrorMessage = "Enter password!")]
        [DataType(DataType.Password)]
        public string Password { get; set; }
    }
}
