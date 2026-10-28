using System.Linq;
using Microsoft.AspNetCore.Mvc.ApplicationModels;
using Volo.Abp.Account;

namespace OpenTms.SignIn;

/// <summary>
/// Takes <c>POST /api/account/register</c> out of the API: users never register themselves
/// (docs/designs/62-sign-in-tenant-detection.md, "No self-registration"). Only that action goes; the rest of ABP's
/// account controller (password reset, profile) stays.
/// </summary>
public class RemoveSelfRegistrationEndpointConvention : IApplicationModelConvention
{
    public void Apply(ApplicationModel application)
    {
        foreach (var controller in application.Controllers.Where(c => c.ControllerType == typeof(AccountController)))
        {
            foreach (var action in controller.Actions.Where(a => a.ActionMethod.Name == nameof(AccountController.RegisterAsync)).ToList())
            {
                controller.Actions.Remove(action);
            }
        }
    }
}
