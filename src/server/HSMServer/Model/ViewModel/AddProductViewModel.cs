using HSMServer.Attributes;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

namespace HSMServer.Model.ViewModel
{
    public sealed class AddProductViewModel
    {
        [Required(ErrorMessage = "{0} is required.")]
        [StringLength(100, ErrorMessage = "{0} length should be less than {1}.")]
        [UniqueValidation(ErrorMessage = "Product with the same name already exists.")]
        [RegularExpression(@"^[0-9a-zA-Z .,_\-=#:;%&*()]*$", ErrorMessage = "{0} contains forbidden characters.")]
        [DisplayName("New product name")]
        public string Name { get; set; }
    }
}
