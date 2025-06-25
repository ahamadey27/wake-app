# WakeAdvisor

A Modern ASP.NET Core Web App for Predicting Optimal Wake Surfing Times Behind Hudson River Freighters

## Project Overview

WakeAdvisor is a web application designed to help users identify the best times to surf the wake of southbound freighters on the Hudson River near Kingston, NY. By integrating real-time tide predictions and live AIS vessel tracking, the app pinpoints windows when low tide (≤ 2 feet) coincides with the approach of a southbound freighter, maximizing wake surfing opportunities. The project is built for learning, personal use, and potential real-world deployment.

## Features

- Real-time tide predictions for Kingston, NY (NOAA CO-OPS API)
- Live AIS vessel tracking and filtering for southbound freighters
- ETA calculation for freighter arrival at Kingston Point
- Combined display of low tide windows and freighter approach times
- Responsive Razor Pages frontend with user-friendly interface
- Configuration via `appsettings.json` and environment variables
- About page describing project purpose and technology stack

## How It Was Built

- **Backend:** ASP.NET Core (MVC/Razor Pages), C#
- **Frontend:** Razor Pages, HTML, CSS, JavaScript
- **APIs:** NOAA CO-OPS (tide data), AIS Stream (freighter data; see note below)
- **Data Processing:** System.Net.Http, Newtonsoft.Json/System.Text.Json
- **Deployment:** Ready for local IIS, Azure App Service, or other cloud platforms

## Core Services

- `TideService.cs`: Fetches and processes tide data, identifies low tide windows
- `FreighterService.cs`: Authenticates with AIS provider, filters for southbound freighters, calculates ETA
- Data models for tide predictions, vessel data, and geolocation

## Configuration

- NOAA and AIS API endpoints and credentials stored in `appsettings.json`
- Kingston Point coordinates and southbound bearing range configurable
- Low tide threshold adjustable

## AIS Data Provider Note

> **Important:** The currently integrated AIS API endpoint may not provide full coverage for the Hudson River region near Kingston, NY. As a result, live freighter tracking may be limited or unavailable depending on the provider's data. I am actively researching alternative AIS data sources or solutions that offer better coverage for this area. Contributions or suggestions are welcome!

## Development & Testing

- Unit and integration tests for core logic (tide filtering, ETA calculation)
- Error handling for API/network issues
- User input validation and feedback
- Deployment guides for cloud and local environments

---

Feel free to use this project as a learning resource or portfolio piece. For setup instructions, API details, and contribution guidelines, see the rest of this README and the included documentation files.
