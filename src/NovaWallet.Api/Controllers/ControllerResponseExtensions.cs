using Microsoft.AspNetCore.Mvc;
using NovaWallet.Api.Contracts;

namespace NovaWallet.Api.Controllers;

internal static class ControllerResponseExtensions
{
    public static ApiResponse<T> ToApiResponse<T>(this ControllerBase controller, T data) =>
        ApiResponse<T>.Ok(data, controller.HttpContext.TraceIdentifier);
}
