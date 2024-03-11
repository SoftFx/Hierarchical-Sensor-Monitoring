using HSMServer.Attributes;
using HSMServer.Model.TreeViewModel;
using System;
using System.ComponentModel.DataAnnotations;

namespace HSMServer.Model.ViewModel
{
    public class ProductGeneralInfoViewModel
    {
        public Guid Id { get; set; }

        [Required(ErrorMessage = "{0} is required.")]
        [StringLength(60, ErrorMessage = "{0} length should be less than {1}.")]
        [UniqueValidation(ErrorMessage = "Folder with the same name already exists.")]
        public string Name { get; set; }

        public string OldName { get; set; }

        public string Description { get; set; }


        public ProductGeneralInfoViewModel() { }

        public ProductGeneralInfoViewModel(ProductNodeViewModel product)
        {
            Id = product.Id;
            Name = product.Name;
            OldName = product.Name;
            Description = product.Description;
        }
    }
}
