using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;
using Swashbuckle.AspNetCore.SwaggerUI;

namespace LibraryApp.Api.OpenApi;

/// <summary>
/// NFR-11: Swagger groups the endpoints by module. Next to the complete document <c>v1</c> (unchanged, with its controller
/// sections) there is one document per module, chosen in the "Select a definition" list of the Swagger UI. The module of
/// an endpoint is the folder of its controller, <c>Controllers/&lt;Module&gt;/</c> (namespace
/// <c>LibraryApp.Api.Controllers.&lt;Module&gt;</c>), so a new controller lands in its module without an attribute.
/// </summary>
public static class ModuleDocuments
{
    /// <summary>The complete document: every endpoint of every module.</summary>
    public const string All = "v1";

    private const string ControllersNamespace = "LibraryApp.Api.Controllers.";

    /// <summary>Document name (the controller folder in lower case) and title of each module, in the order of the requirements.</summary>
    public static readonly IReadOnlyList<(string Name, string Folder, string Title)> Modules =
    [
        ("catalog", "Catalog", "Catalog"),
        ("staff", "Staff", "Staff Members"),
        ("borrowing", "Borrowing", "Borrowing & Return"),
        ("notifications", "Notifications", "Notifications"),
        ("reporting", "Reporting", "Reporting"),
    ];

    public static void AddDocuments(SwaggerGenOptions options)
    {
        options.SwaggerDoc(All, new OpenApiInfo
        {
            Title = "LibraryApp.Api",
            Version = "1.0",
            Description = "Every endpoint. One document per module is in the \"Select a definition\" list.",
        });
        foreach (var (name, _, title) in Modules)
            options.SwaggerDoc(name, new OpenApiInfo { Title = $"LibraryApp.Api — {title}", Version = "1.0" });

        options.DocInclusionPredicate((document, api) => document == All || document == ModuleOf(api));
    }

    /// <summary>The complete document first (it opens by default), then the modules.</summary>
    public static void AddEndpoints(SwaggerUIOptions options)
    {
        options.SwaggerEndpoint($"/swagger/{All}/swagger.json", "All modules");
        foreach (var (name, _, title) in Modules)
            options.SwaggerEndpoint($"/swagger/{name}/swagger.json", title);
    }

    /// <summary>The document name of the endpoint's module, or null for an endpoint outside the module folders.</summary>
    public static string? ModuleOf(ApiDescription api)
    {
        if (api.ActionDescriptor is not ControllerActionDescriptor action)
            return null;

        string? ns = action.ControllerTypeInfo.Namespace;
        if (ns is null || !ns.StartsWith(ControllersNamespace, StringComparison.Ordinal))
            return null;

        string folder = ns[ControllersNamespace.Length..].Split('.')[0];
        return Modules.FirstOrDefault(module => module.Folder == folder).Name;
    }
}
