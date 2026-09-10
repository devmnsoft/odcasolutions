using System.ComponentModel.DataAnnotations;
using Odca.Contracts.Tenancy;
namespace Odca.Web.Models;
public sealed record TeamPageViewModel(OrganizationSummary Organization,TeamMemberResponse[] Members,TenantRoleResponse[] Roles,string? Search,string? Status);
public sealed class InviteViewModel
{
 [Required,EmailAddress] public string Email {get;set;}=string.Empty;
 [Required] public Guid RoleId {get;set;}
 [Required] public Guid TenantId {get;set;}
}
