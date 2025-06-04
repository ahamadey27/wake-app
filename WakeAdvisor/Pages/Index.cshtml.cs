using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using WakeAdvisor.Services;

namespace WakeAdvisor.Pages;

public class IndexModel : PageModel
{
    private readonly ILogger<IndexModel> _logger;
    private readonly TideService _tideService;

    public IndexModel(ILogger<IndexModel> logger, TideService tideService)
    {
        _logger = logger;
        _tideService = tideService;
    }

    [BindProperty]
    public DateTime SelectedDate { get; set; }

    public List<LowTideWindow>? LowTideWindows { get; set; }

    public void OnGet()
    {

    }

    public async Task<IActionResult> OnPostAsync()
    {
        // Call the TideService to get low tide windows for the selected date
        LowTideWindows = await _tideService.GetLowTideWindowsAsync(SelectedDate);
        return Page();
    }
}
