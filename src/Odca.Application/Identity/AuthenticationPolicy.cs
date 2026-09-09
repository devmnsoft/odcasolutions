namespace Odca.Application.Identity;

public sealed record AuthenticationPolicy(TimeSpan SessionLifetime);
