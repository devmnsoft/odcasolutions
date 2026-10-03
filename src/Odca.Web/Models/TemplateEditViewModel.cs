namespace Odca.Web.Models;

public sealed class TemplateEditViewModel
{
    public Guid? TemplateId { get; set; }
    public Guid TenantId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string ContractType { get; set; } = "services";
    public string Scope { get; set; } = "private";
    public string Status { get; set; } = "draft";
    public int Version { get; set; } = 1;
    public long RowVersion { get; set; } = 1;
    public string ContentJson { get; set; } = string.Empty;
    public string FieldsJson { get; set; } = string.Empty;
    public string? ErrorMessage { get; set; }
    public string? ServerName { get; set; }
    public string? ServerDescription { get; set; }
    public long ServerRowVersion { get; set; }
    public bool IsNew => !TemplateId.HasValue || TemplateId == Guid.Empty;
}
