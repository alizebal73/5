namespace GameNet.Server.Modules.Sessions.Domain;

public enum SessionStatus
{
    Active = 1,
    PendingSettlement = 2,
    Paid = 3,
    Paused = 4
}
