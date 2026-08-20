using System.Security.Claims;

namespace Test.Web.Api.Services;

public interface IAdAccessChecker
{
    bool IsAuthorized(ClaimsPrincipal principal);
}
