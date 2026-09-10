using System.ComponentModel.DataAnnotations;
using Odca.Contracts.Tenancy;
namespace Odca.Web.Models;
public sealed record TeamPageViewModel(OrganizationSummary Organization,TeamMemberResponse[] Members,TenantRoleResponse[] Roles,string? Search,string? Status,string InviteEmail,Guid? InviteRoleId,string IdempotencyKey);
public sealed class InviteViewModel
{
 [Required,EmailAddress] public string Email {get;set;}=string.Empty;
 [Required] public Guid RoleId {get;set;}
 [Required] public Guid TenantId {get;set;}
 [Required,StringLength(100)] public string IdempotencyKey {get;set;}=string.Empty;
}
