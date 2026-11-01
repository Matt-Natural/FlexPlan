using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;

namespace FlexPlan.Controllers;

public abstract class BaseController : Controller
{
    protected int UserId => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
}
