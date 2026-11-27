using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Reflection;
using HSMServer.Model.AccessKeysViewModels;
using HSMServer.Model.AlertSchedule;
using HSMServer.Model.Folders.ViewModels;
using HSMServer.Model.ManagementApi.AlertSchedules;
using HSMServer.Model.Notifications;
using HSMServer.Model.ViewModel;
using HSMServer.Notifications.Chats;
using Xunit;

namespace HSMServer.Core.Tests.Validation
{
    // #1934: every form's name-length rule accepts a name of exactly its maximum and refuses
    // the next character, and its message says so — "at most N characters", where it read
    // "less than N" while a name of N characters went through.
    public class NameLengthRuleTests
    {
        public static IEnumerable<object[]> Forms()
        {
            yield return [typeof(ChatViewModel), nameof(ChatViewModel.Name), ChatNameValidator.NameMaxLength];
            yield return [typeof(AddProductViewModel), nameof(AddProductViewModel.Name), 100];
            yield return [typeof(ProductGeneralInfoViewModel), nameof(ProductGeneralInfoViewModel.Name), 100];
            yield return [typeof(EditAccessKeyViewModel), nameof(EditAccessKeyViewModel.DisplayName), 100];
            yield return [typeof(EditFolderViewModel), nameof(EditFolderViewModel.Name), 60];
            yield return [typeof(AlertScheduleViewModel), nameof(AlertScheduleViewModel.Name), AlertScheduleAdministrationService.MaxNameLength];
        }

        [Theory]
        [MemberData(nameof(Forms))]
        public void TheLengthRule_AcceptsExactlyItsMaximum_AndItsMessageSaysSo(Type form, string property, int maximum)
        {
            var length = form.GetProperty(property).GetCustomAttribute<StringLengthAttribute>();
            var context = new ValidationContext(Activator.CreateInstance(form)!) { MemberName = property };

            Assert.Equal(maximum, length.MaximumLength);
            Assert.Same(ValidationResult.Success, length.GetValidationResult(new string('n', maximum), context));

            var tooLong = length.GetValidationResult(new string('n', maximum + 1), context);

            Assert.NotSame(ValidationResult.Success, tooLong);
            Assert.EndsWith($"at most {maximum} characters.", tooLong.ErrorMessage);
        }
    }
}
