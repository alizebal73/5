using GameNet.Agent;
namespace GameNet.Agent.Tests;
public sealed class AgentFoundationTests
{
    [Fact] public void Agent_worker_type_exists() => Assert.NotNull(typeof(AgentWorker));
}
