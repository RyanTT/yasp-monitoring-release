using Microsoft.AspNetCore.Components;

using Nito.AsyncEx;

using YASP.Client.Application.Pages;
using YASP.Client.Application.Pages.Objects;
using YASP.Shared.Objects;

namespace YASP.Client.Pages;

public partial class Index : IDisposable
{

    [Parameter]
    public string PageId { get; set; }

    [Inject]
    public StatusPageDataService DataService { get; set; }

    public StatusPageData Data { get; set; }
    public bool IsLoading { get; set; } = true;
    public bool IsPageFound { get; set; }

    public bool IsRefreshing { get; set; }
    public DateTimeOffset NextRefresh { get; set; }
    public DateTimeOffset LastRefresh { get; set; }

    public (int, int, string, DateTimeOffset) ShowDetails { get; set; }

    private AsyncSemaphore _refreshThreadSemaphore = new AsyncSemaphore(1);
    private CancellationTokenSource _cancellationTokenSource = new CancellationTokenSource();
    private CancellationTokenSource _automaticRefreshTokenSource = new CancellationTokenSource();

    protected override async Task OnInitializedAsync()
    {
        NextRefresh = DateTimeOffset.Now.AddMinutes(1);

        await RefreshAsync();
    }

    public async Task RefreshAsync(bool enqueueAutomaticRefresh = true)
    {
        _automaticRefreshTokenSource.Cancel();

        if (_refreshThreadSemaphore.CurrentCount == 0) return; // If we are already refreshing, abort (this could be user initiated)

        await _refreshThreadSemaphore.LockAsync(_cancellationTokenSource.Token);

        IsRefreshing = true;
        LastRefresh = DateTimeOffset.Now;

        StateHasChanged();

        await Task.Delay(1000);

        bool dataReceived = false;

        while (!dataReceived)
        {
            try
            {
                var pageDto = await DataService.GetPageDataAsync(PageId, _cancellationTokenSource.Token);
                Data = await DataService.PrepareSegmentsDataAsync(pageDto);

                IsPageFound = true;
                dataReceived = true;
            }
            catch (HttpRequestException ex)
            {
                if (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    IsPageFound = false;
                    IsLoading = false;

                    break;
                }
            }
        }

        NextRefresh = DateTimeOffset.Now.AddMinutes(1);
        IsRefreshing = false;
        IsLoading = false;

        StateHasChanged();

        // Start new thread that will refresh automatically
        if (enqueueAutomaticRefresh && IsPageFound) EnqueueRefresh();

        _refreshThreadSemaphore.Release();
    }
    private void ShowSegmentTooltip(int categoryIndex, int monitorIndex, string monitorId, DateTimeOffset from)
    {
        ShowDetails = (categoryIndex, monitorIndex, monitorId, from);

        StateHasChanged();
    }

    private void HideSegmentTooltip()
    {
        ShowDetails = default;

        StateHasChanged();
    }

    private void EnqueueRefresh()
    {
        _automaticRefreshTokenSource?.Cancel();
        _automaticRefreshTokenSource = new CancellationTokenSource();

        _ = Task.Run(async () => await EnqueueRefreshAsync());
    }

    private async Task EnqueueRefreshAsync()
    {
        while (DateTimeOffset.Now < NextRefresh)
        {
            await Task.Delay(TimeSpan.FromSeconds(1), _automaticRefreshTokenSource.Token);

            StateHasChanged();
        }

        await InvokeAsync(() => RefreshAsync());
    }

    public void Dispose()
    {
        _cancellationTokenSource.Cancel();
    }
}