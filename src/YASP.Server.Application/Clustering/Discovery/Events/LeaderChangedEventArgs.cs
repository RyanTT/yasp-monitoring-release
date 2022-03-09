namespace YASP.Server.Application.Clustering.Discovery.Events
{
    public class LeaderChangedEventArgs
    {
        public Node Node { get; set; }
        public bool IsLocal { get; set; }
    }
}
