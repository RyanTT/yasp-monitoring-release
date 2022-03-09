using MediatR;

using YASP.Server.Application.Clustering.Communication;
using YASP.Server.Application.Monitoring.Objects;

namespace YASP.Server.Application.Monitoring.Timeline.Messages
{
    /// <summary>
    /// Network request that is used by a node to inform the leader of the monitors for which that node has timeline segments for that should be analyzed.
    /// </summary>
    public class TimelinesUpdatedRequest : RequestBase<Unit>
    {
        public List<MonitorId> MonitorIds { get; set; }

        public class Handler : IRequestHandler<TimelinesUpdatedRequest>
        {
            private readonly TimelineAnalyzerService _timelineAnalyzerService;

            public Handler(TimelineAnalyzerService timelineAnalyzerService)
            {
                _timelineAnalyzerService = timelineAnalyzerService;
            }

            public async Task<Unit> Handle(TimelinesUpdatedRequest request, CancellationToken cancellationToken)
            {
                await _timelineAnalyzerService.StartAnalysisForMonitorsIfNecessaryAsync(request.MonitorIds);

                return Unit.Value;
            }
        }
    }
}
