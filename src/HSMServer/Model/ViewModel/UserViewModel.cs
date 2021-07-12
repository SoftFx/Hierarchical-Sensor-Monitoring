﻿using HSMServer.Authentication;
using System.Collections.Generic;

namespace HSMServer.Model.ViewModel
{
    public class UserViewModel
    {
        public string UserId { get; set; }
        public string Username { get; set; }
        public string Password { get; set; }
        public bool IsAdmin { get; set; }
        public List<KeyValuePair<string, ProductRoleEnum>> ProductsRoles { get; set; }
        public UserViewModel(User user)
        {
            UserId = user.Id.ToString();
            Username = user.UserName;
            Password = user.Password;
            IsAdmin = user.IsAdmin;
            ProductsRoles = user.ProductsRoles;
        }
        public UserViewModel()
        {

        }
    }
}
