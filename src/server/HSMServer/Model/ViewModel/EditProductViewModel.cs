using HSMServer.Core.Model.Authentication;
using HSMServer.Model.AccessKeysViewModels;
using HSMServer.Model.TreeViewModels;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace HSMServer.Model.ViewModel
{
    public class EditProductViewModel
    {
        private readonly List<UserViewModel> _usedUsers;


        public List<AccessKeyViewModel> AccessKeys { get; }

        public List<(UserViewModel, ProductRoleEnum)> UsersRights { get; }

        public HashSet<UserViewModel> NotAdminUsers { get; }

        public TelegramSettingsViewModel Telegram { get; }

        public string ProductName { get; }

        public Guid ProductId { get; }

        public string EncodedProductId { get; }


        public EditProductViewModel(ProductNodeViewModel product,
                                    List<(User, ProductRoleEnum)> usersRights,
                                    List<User> notAdminUsers)
        {
            ProductName = product.Name;
            ProductId = product.Id;
            EncodedProductId = product.EncodedId;

            UsersRights = usersRights.Select(x => (new UserViewModel(x.Item1), x.Item2)).ToList();
            AccessKeys = product.GetAccessKeys();
            Telegram = product.TelegramSettings;

            _usedUsers = UsersRights.Select(ur => ur.Item1).ToList();
            NotAdminUsers = notAdminUsers.Select(x => new UserViewModel(x)).ToHashSet();
            NotAdminUsers.ExceptWith(_usedUsers);
            
            Telegram.MinStatusLevelHelper = GetStatusPairs();
        }

        private string GetStatusPairs()
        {
            var minStatusLevel = (int)Telegram.MinStatusLevel;
            var length = Enum.GetValues<SensorStatus>().Length;
            
            var builder = new StringBuilder(1 << 4);
         
            for (int i = minStatusLevel; i < length - 1; i++)
            {
                for (int j = i + 1; j < length; j++)
                {
                    builder.Append($"{(SensorStatus)i} -> {(SensorStatus)j}, ")
                           .Append($"{(SensorStatus)j} -> {(SensorStatus)i}, ");
                }

            }

            var response = builder.ToString();
            return string.IsNullOrEmpty(response) ? response : response[..^2];
        }
    }
}