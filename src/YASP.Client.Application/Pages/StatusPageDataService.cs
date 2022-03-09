using System.Net;
using System.Net.Http.Json;

using YASP.Client.Application.Pages.Objects;
using YASP.Shared.Objects;

namespace YASP.Client.Application.Pages
{
    /// <summary>
    /// Service that fetches status page data from the REST api of the node that served this frontend.
    /// </summary>
    public class StatusPageDataService
    {
        private readonly HttpClient _httpClient;

        public StatusPageDataService(HttpClient httpClient)
        {
            _httpClient = httpClient;
        }

        /// <summary>
        /// Requests the data from the api for the given <paramref name="pageId"/>.
        /// </summary>
        /// <param name="pageId"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public async Task<PageDto> GetPageDataAsync(string pageId, CancellationToken cancellationToken = default)
        {
            return await _httpClient.GetFromJsonAsync<PageDto>($"/api/pages/{WebUtility.UrlEncode(pageId)}", cancellationToken);
        }

        /// <summary>
        /// Prepares the data into a format that is easier to iterate through inside the UI.
        /// Splices the data into hourly segments and determines per segment the worst status the monitor was in.
        /// </summary>
        /// <param name="pageDto"></param>
        /// <returns></returns>
        public Task<StatusPageData> PrepareSegmentsDataAsync(PageDto pageDto)
        {
            var values = new StatusPageData
            {
                ApiData = pageDto
            };

            foreach (var monitor in pageDto.Categories.SelectMany(x => x.Monitors))
            {
                if (values.Segments.ContainsKey(monitor.MonitorId)) continue;

                var segments = new List<StatusPageData.Segment>();
                values.Segments.Add(monitor.MonitorId, segments);

                var status = MonitorStatusEnumDto.Unknown;

                var firstState = monitor.States.Where(x => x.CheckTimestamp <= DateTimeOffset.UtcNow.AddDays(-1)).OrderBy(x => x.CheckTimestamp).FirstOrDefault();

                if (firstState != null)
                {
                    status = firstState.MonitorStatus;
                }

                for (DateTimeOffset iteratorDate = DateTimeOffset.UtcNow.AddDays(-1).AddHours(1);
                    iteratorDate <= DateTimeOffset.UtcNow;
                    iteratorDate = iteratorDate.AddHours(1))
                {
                    var startTime = new DateTimeOffset(iteratorDate.Year, iteratorDate.Month, iteratorDate.Day,
                    iteratorDate.Hour, 0, 0, TimeSpan.Zero);
                    var endTime = startTime.AddHours(1).AddTicks(-1);

                    var states = monitor.States.Where(x => x.CheckTimestamp >= startTime && x.CheckTimestamp <= endTime).ToList();

                    var initialStatus = status;

                    // Find the lowest status for this time segment
                    status = FindLowestStatus(states.Select(x => x.MonitorStatus).Append(status));

                    // If we have a check that is exactly at the start time, we overwrite the status with it
                    if (states.Any(x => x.CheckTimestamp == startTime))
                    {
                        var statusAtStartOfTimeframe = states.First(x => x.CheckTimestamp == startTime).MonitorStatus;

                        status = statusAtStartOfTimeframe;
                        initialStatus = statusAtStartOfTimeframe;
                    }

                    // Add segment to list
                    segments.Add(new StatusPageData.Segment
                    {
                        MonitorId = monitor.MonitorId,
                        From = startTime,
                        To = endTime.AddTicks(1),
                        LowestStatus = status,
                        InitialStatus = initialStatus,
                        StateChanges = states.ToList()
                    });

                    // Set status to the latest status we know of
                    var lastState = states.LastOrDefault();

                    if (lastState != null) status = lastState.MonitorStatus;
                }
            }

            return Task.FromResult(values);
        }

        private MonitorStatusEnumDto FindLowestStatus(IEnumerable<MonitorStatusEnumDto> statuses)
        {
            var lowest = MonitorStatusEnumDto.Unknown;

            foreach (var status in statuses)
            {
                if (lowest == MonitorStatusEnumDto.Unknown)
                {
                    lowest = status;
                    continue;
                }

                if (status == MonitorStatusEnumDto.PartiallyReachable && status == MonitorStatusEnumDto.Reachable)
                {
                    lowest = status;
                }

                if (status == MonitorStatusEnumDto.NotReachable)
                {
                    lowest = status;
                }
            }

            return lowest;
        }
    }
}
