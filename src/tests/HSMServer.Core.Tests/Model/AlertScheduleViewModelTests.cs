using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using HSMServer.Model.AlertSchedule;
using HSMServer.Model.ManagementApi.AlertSchedules;
using Xunit;

namespace HSMServer.Core.Tests.Model
{
    // #1597: the web form's name field (AlertScheduleViewModel.Name) must be
    // bounded by the same limit the schedule write engine enforces — a name
    // accepted by the cookie UI but over the API limit could not be updated
    // through the management API afterwards (422 on 'name').
    public class AlertScheduleViewModelTests
    {
        private static bool NameIsValid(string name, out ValidationResult result)
        {
            var results = new List<ValidationResult>();
            var model = new AlertScheduleViewModel();

            var valid = Validator.TryValidateProperty(name,
                new ValidationContext(model) { MemberName = nameof(AlertScheduleViewModel.Name) }, results);

            result = results.FirstOrDefault();
            return valid;
        }


        [Fact]
        public void Name_StringLengthLimit_MatchesAdministrationContract()
        {
            // Drift guard: the attribute references the writer's constant, so
            // the UI form and the API cannot diverge on the allowed length.
            var attribute = typeof(AlertScheduleViewModel)
                .GetProperty(nameof(AlertScheduleViewModel.Name))!
                .GetCustomAttributes(typeof(StringLengthAttribute), inherit: false)
                .OfType<StringLengthAttribute>()
                .Single();

            Assert.Equal(AlertScheduleAdministrationService.MaxNameLength, attribute.MaximumLength);
        }

        [Fact]
        public void Name_AtLimit_PassesValidation()
        {
            // The limit itself is inclusive: a 200-char name is a valid
            // schedule name on every write surface (REST/MCP/UI).
            Assert.True(NameIsValid(new string('n', AlertScheduleAdministrationService.MaxNameLength), out _));
        }

        [Fact]
        public void Name_OverLimit_FailsValidationWithFieldError()
        {
            Assert.False(NameIsValid(new string('n', AlertScheduleAdministrationService.MaxNameLength + 1),
                out var result));

            // The error is keyed on the Name field, so SavePartial's
            // ModelState.IsValid gate re-renders the form with the message
            // under the input instead of persisting the overlong name.
            Assert.Contains(nameof(AlertScheduleViewModel.Name), result!.MemberNames);
        }
    }
}
