namespace GameNet.Server.Infrastructure.Security
{
    public sealed class AgentCredentialException : Exception
    {
        public AgentCredentialException(string code)
            : base(code)
        {
            if (string.IsNullOrWhiteSpace(code))
                throw new ArgumentException("Error code is required.", nameof(code));

            Code = code;
        }

        public string Code { get; }
    }
}
