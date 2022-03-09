
using MediatR;

using YASP.Server.Application.Clustering.Communication;
using YASP.Server.Application.Monitoring.Objects;
using YASP.Server.Application.Monitoring.Timeline;

namespace YASP.Server.Application.Monitoring.Messages
{
    /// <summary>
    /// Network request that gets the timeline data for one monitor from a node.
    /// </summary>
    public class GetTimelineRequest : RequestBase<GetTimelineRequest.Response>
    {
        /// <summary>
        /// The monitor for which the timeline data should be fetched.
        /// </summary>
        public MonitorId MonitorId { get; set; }

        public class Handler : IRequestHandler<GetTimelineRequest, Response>
        {
            private readonly MonitorTimelineService _monitorTimelineService;

            public Handler(MonitorTimelineService monitorTimelineService)
            {
                _monitorTimelineService = monitorTimelineService;
            }

            public async Task<Response> Handle(GetTimelineRequest request, CancellationToken cancellationToken)
            {
                var events = await _monitorTimelineService.GetAsync(request.MonitorId);

                return new Response
                {
                    Timeline = events
                };
            }
        }

        public class Response
        {
            /// <summary>
            /// Timeline data.
            /// </summary>
            public List<MonitorTimelineService.TimelineSegment> Timeline { get; set; }
        }
    }
}
