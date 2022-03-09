namespace YASP.Server.Application.Monitoring.Objects
{
    [Serializable]
    public enum MonitorStatusEnum
    {
        Unknown,
        Reachable,
        NotReachable,
        PartiallyReachable
    }
}
