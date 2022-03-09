using AutoMapper;

using YASP.Server.Application.Monitoring.Objects;
using YASP.Server.Application.Pages;
using YASP.Server.Application.Persistence.Objects;
using YASP.Shared.Objects;

namespace YASP.Server.Application.Mapping
{
    /// <summary>
    /// Profile for AutoMapper to setup the mapping between domain objects and DTOs.
    /// </summary>
    public class ApiProfile : Profile
    {
        public ApiProfile()
        {
            CreateMap<PageConfiguration, PageDto>();
            CreateMap<PageConfiguration.PageCategory, PageCategoryDto>();
            CreateMap<PageConfiguration.PageCategory.MonitorEntry, PageCategoryMonitorDto>();
            CreateMap<MonitorStatusEnum, MonitorStatusEnumDto>();
            CreateMap<MonitorState, MonitorStateDto>()
                .ForMember(x => x.MonitorId, config => config.MapFrom(x => x.MonitorId.Identifier));
        }
    }
}
