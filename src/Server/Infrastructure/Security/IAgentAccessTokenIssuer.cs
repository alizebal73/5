using GameNet.Shared.Contracts.V1.Security;

namespace GameNet.Server.Infrastructure.Security;

public interface IAgentAccessTokenIssuer
{
    AgentTokenResponse Issue(string deviceId);
}
