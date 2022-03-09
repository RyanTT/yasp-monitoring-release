using AutoMapper;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using YASP.Server.Application.Pages;
using YASP.Shared.Objects;

namespace YASP.Server.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [AllowAnonymous]
    public class PagesController : ControllerBase
    {
        private readonly PageValuesProvider _pageValuesProvider;

        public PagesController(PageValuesProvider pageValuesProvider)
        {
            _pageValuesProvider = pageValuesProvider;
        }

        public IMapper Mapper { get; }

        [HttpGet("{pageId}")]
        public async Task<ActionResult<PageDto>> GetPageDataAsync([FromRoute] string pageId)
        {
            try
            {
                return await _pageValuesProvider.GetPageDtoAsync(pageId);
            }
            catch (KeyNotFoundException)
            {
                return NotFound();
            }
        }
    }
}
