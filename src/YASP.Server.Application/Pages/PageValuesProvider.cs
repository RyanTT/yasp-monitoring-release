using AutoMapper;

using YASP.Server.Application.Persistence.Objects;
using YASP.Server.Application.State;
using YASP.Shared.Objects;

namespace YASP.Server.Application.Pages
{
    /// <summary>
    /// Simple service that gathers the data for a status page and maps it to DTOs.
    /// </summary>
    public class PageValuesProvider
    {
        private readonly ApplicationStateService _applicationStateService;
        private readonly IMapper _mapper;

        public PageValuesProvider(ApplicationStateService applicationStateService, IMapper mapper)
        {
            _applicationStateService = applicationStateService;
            _mapper = mapper;
        }

        /// <summary>
        /// Gets the data for the specified <paramref name="pageId"/> as DTOs.
        /// </summary>
        /// <param name="pageId"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        /// <exception cref="KeyNotFoundException"></exception>
        public async Task<PageDto> GetPageDtoAsync(string pageId, CancellationToken cancellationToken = default)
        {
            var appState = await _applicationStateService.GetSnapshotAsync(cancellationToken);
            var pageConfig = appState.AppConfiguration.Pages.FirstOrDefault(x => x.Id.ToLower() == pageId?.ToLower());

            // No status page exists with this ID..
            if (pageConfig == null) throw new KeyNotFoundException($"Page '{pageId}' not found.");

            var pageDto = _mapper.Map<PageDto>(pageConfig);

            foreach (var category in pageDto.Categories)
            {
                foreach (var monitor in category.Monitors)
                {
                    var states = appState.MonitorStates.Where(state => state.MonitorId == monitor.MonitorId).OrderBy(x => x.CheckTimestamp).ToList();
                    var filteredStates = new List<MonitorState>();

                    // Filter out all status changes that are redundant entries (same as previous entry) just to clean up a little when sending the data out via the REST api
                    foreach (var state in states)
                    {
                        if (filteredStates.Count == 0)
                        {
                            filteredStates.Add(state);
                            continue;
                        }

                        var previousState = filteredStates.Last();

                        if (previousState.MonitorStatus == state.MonitorStatus) continue;

                        filteredStates.Add(state);
                    }

                    monitor.States = _mapper.Map<List<MonitorState>, List<MonitorStateDto>>(filteredStates);
                }
            }

            return pageDto;
        }
    }
}
