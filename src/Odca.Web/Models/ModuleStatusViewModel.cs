namespace Odca.Web.Models;

public sealed record ModuleStatusViewModel(
    string Title,
    string Description,
    string NextStep,
    bool IsPlatformModule,
    string? OpenHref = null,
    string? OpenLinkText = null);
