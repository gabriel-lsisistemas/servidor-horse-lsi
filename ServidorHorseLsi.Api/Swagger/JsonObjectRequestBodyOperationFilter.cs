using Microsoft.OpenApi.Any;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace ServidorHorseLsi.Api.Swagger;

/// <summary>
/// Endpoints leem JSON do body via JsonBodyHelper (sem [FromBody]).
/// Este filtro adiciona o campo de JSON no Swagger para testar com Try it out.
/// </summary>
public sealed class JsonObjectRequestBodyOperationFilter : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        var method = context.ApiDescription.HttpMethod;
        if (!string.Equals(method, "POST", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(method, "PUT", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (operation.RequestBody is not null)
            return;

        operation.RequestBody = new OpenApiRequestBody
        {
            Required = false,
            Description = "JSON livre (os campos dependem de cada rota, em geral cnpj).",
            Content =
            {
                ["application/json"] = new OpenApiMediaType
                {
                    Schema = new OpenApiSchema
                    {
                        Type = "object",
                        AdditionalPropertiesAllowed = true,
                        Example = new OpenApiObject
                        {
                            ["cnpj"] = new OpenApiString("00000000000000")
                        }
                    }
                }
            }
        };
    }
}
