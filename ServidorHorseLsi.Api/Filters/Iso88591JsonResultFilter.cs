using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using ServidorHorseLsi.Api.Infrastructure;

namespace ServidorHorseLsi.Api.Filters;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class Iso88591JsonAttribute : ResultFilterAttribute
{
    public override void OnResultExecuting(ResultExecutingContext context)
    {
        if (context.Result is not ObjectResult objectResult || objectResult.Value is null)
            return;

        var json = JsonRowHelper.SerializeIso88591(objectResult.Value);
        var encoding = Encoding.GetEncoding("iso-8859-1");
        var bytes = encoding.GetBytes(json);

        context.HttpContext.Response.StatusCode = objectResult.StatusCode ?? StatusCodes.Status200OK;
        context.Result = new FileContentResult(bytes, "application/json; charset=iso-8859-1");
    }

}
