using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using WakeAdvisor.Services;

namespace WakeAdvisor.Pages;

public class IndexModel : PageModel
{
    private readonly TideService _tideService;
    private readonly FreighterService _freighterService;

    public List<LowTideWindow>? LowTideWindows { get; set; }
    public List<FreighterInfo>? FreighterInfos { get; set; }
    public string SelectedDay { get; set; } = "today";

    public IndexModel(TideService tideService, FreighterService freighterService)
    {
        _tideService = tideService;
        _freighterService = freighterService;
        LowTideWindows = new List<LowTideWindow>();
        FreighterInfos = new List<FreighterInfo>();
    }

    public async Task OnGetAsync()
    {
        await LoadDataAsync("today");
    }

    public async Task OnPostAsync()
    {
        SelectedDay = Request.Form["SelectedDay"].ToString() ?? "today";
        await LoadDataAsync(SelectedDay);
    }

    private async Task LoadDataAsync(string selectedDay)
    {
        DateTime date = DateTime.Now.Date;
        if (selectedDay == "tomorrow")
        {
            date = date.AddDays(1);
        }
        LowTideWindows = await _tideService.GetLowTideWindowsAsync(date);
        FreighterInfos = await _freighterService.GetSouthboundFreighterInfoAsync(date);
    }
}
