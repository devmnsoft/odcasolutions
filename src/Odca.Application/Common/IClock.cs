namespace Odca.Application.Common;

public interface IClock
{
    DateTimeOffset UtcNow { get; }
}
