using System;
using System.Collections.Generic;
using System.Linq;
using HSMServer.Core.Cache;
using HSMServer.Core.Model;

namespace HSMServer.Model.DataAlertTemplates
{
    // #1210 path/type mismatch validation, shared by the cookie UI controller
    // (AlertTemplatesController) and the REST controller (AlertTemplatesApiController):
    // both surfaces must reject with the SAME message, so the rule lives in one place
    // and cannot drift.
    public static class AlertTemplatePathValidation
    {
        // A path template matching sensors of a type incompatible with the template's
        // concrete type is rejected — anything flagged here would be silently skipped
        // at apply time (AlertTemplateModel.IsMatch). AnyType templates match every
        // type and skip the check. A path matching nothing is allowed (the template
        // may precede its sensors).
        public static List<string> GetPathTypeMismatchErrors(ITreeValuesCache cache, AlertTemplateModel model)
        {
            var errors = new List<string>();
            var templateType = model.GetSensorType();

            if (!templateType.HasValue)
                return errors;

            var templateTypeName = templateType.Value.ToString().Replace("Sensor", string.Empty);

            foreach (var path in model.Paths.Where(p => !string.IsNullOrWhiteSpace(p)))
            {
                var mismatched = cache.GetSensors(path, null, model.FolderId)
                    .FirstOrDefault(s => s.Type != templateType.Value);

                if (mismatched != null)
                {
                    var mismatchedTypeName = mismatched.Type.ToString().Replace("Sensor", string.Empty);
                    errors.Add($"Path \"{path}\" matches {mismatchedTypeName} sensors, but this template is configured for {templateTypeName} sensors. Use a separate Alert Template for another sensor type.");
                }
            }

            return errors;
        }
    }
}
