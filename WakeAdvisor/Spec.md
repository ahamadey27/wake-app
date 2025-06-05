# Project: Hudson River Southbound Freighter Wake Advisor

**Goal:** To develop an ASP.NET web application that identifies optimal times for wake surfing behind southbound freighters on the Hudson River near Kingston, NY, by correlating low tide conditions (≤ 2 feet) with predicted southbound freighter approaches. The application now only supports tide predictions for today's date (no future date predictions).

---

# Components

## Environment/Hosting
- **Local Development Machine:** Windows/macOS/Linux (assumed)
- **IDE:** Visual Studio or Visual Studio Code
- **Version Control:** Git
- **Cloud Hosting (Potential):** Azure App Service, AWS Elastic Beanstalk, Virtual Private Server (VPS), or local IIS for personal use.

## Software Components

### Web Application Backend & Frontend
- **Framework:** ASP.NET Core (MVC, Razor Pages, or Blazor)
- **Language:** C#
- **Frontend Technologies:** HTML, CSS, JavaScript (for user interface and interactions)
- **API Interaction:** System.Net.Http (for calling external APIs)
- **JSON Processing:** Newtonsoft.Json or System.Text.Json

### Core Logic Services
- `TideService.cs` (Handles NOAA tide data retrieval and processing)
- `FreighterService.cs` (Handles AIS freighter data retrieval, filtering, and ETA calculation)

### External APIs
- **NOAA CO-OPS API:** For fetching tide prediction data (Station ID: 8519482 "Kingston Point, Hudson River, NY").
- **AIS Data Provider API:** For fetching freighter information (e.g., MarineTraffic API, VesselFinder API, AISHub, Spire Maritime - provider to be selected).

---

# Core Services and Data Structures

## `TideService.cs` (Service)
- **Responsibilities:**
    - Accepts a target date and location (Kingston, NY; NOAA Station ID 8519482).
    - Constructs and executes API requests to the NOAA CO-OPS API.
    - Parses JSON responses from the NOAA API.
    - Filters tide predictions to identify time windows when tide height is ≤ 2 feet (MLLW).
- **Key Methods (Conceptual):**
    - `GetLowTideWindowsAsync(DateTime date)`: Returns a list of suitable low-tide periods.
- **Implied Data Models:**
    - `TidePredictionData` (class to model raw data from NOAA API, e.g., timestamp, tide height).
    - `LowTideWindow` (class to represent a period of low tide, e.g., start time, end time, average/min height).

## `FreighterService.cs` (Service)
- **Responsibilities:**
    - Authenticates with and calls the chosen AIS Data Provider API.
    - Retrieves vessel data for a geographic area encompassing Kingston, NY.
    - Filters AIS data to identify southbound freighters approaching Kingston.
    - Calculates Estimated Time of Arrival (ETA) for relevant freighters at Kingston Point.
    - Handles logic for near-term (live AIS calls) vs. far-future date requests.
- **Key Methods (Conceptual):**
    - `GetSouthboundFreighterInfoAsync(DateTime selectedDate, GeoPoint kingstonReferencePoint)`: Returns information on detected southbound freighters and their ETAs.
- **Implied Data Models:**
    - `AISVesselData` (class to model raw vessel data from AIS API, e.g., MMSI, vessel type, latitude, longitude, SOG, COG/HDG).
    - `FreighterInfo` (class to represent a filtered southbound freighter, e.g., name, ETA at Kingston, current SOG).
    - `GeoPoint` (struct or class for latitude/longitude coordinates).

## Configuration (`appsettings.json` / Environment Variables)
- **NOAA API Endpoint:** URL for the tide data service.
- **AIS API Endpoint & Key:** URL and authentication credentials for the chosen AIS provider.
- **Kingston Point Coordinates:** Latitude/Longitude for the reference point.
- **Southbound Bearing Range:** Min/Max compass degrees defining "southbound".
- **Low Tide Threshold:** Maximum tide height (e.g., 2 feet).

---

# Development Plan

## Phase 1: Core Setup & Tide Data Integration
- [x] **Step 1.1: Project Initialization & Environment Setup**
    - [x] Create new ASP.NET Core project.
    - [x] Initialize Git repository.
    - [x] Install NuGet packages: System.Net.Http, Newtonsoft.Json/System.Text.Json.
- [x] **Step 1.2: Basic User Interface (UI) Design**
    - [x] Develop minimal frontend (date input, submit button, results display area).
    - [x] Restrict UI to only allow today's date for tide predictions.
- [x] **Step 1.3: Implement Tide Service (TideService.cs)**
    - [x] Create `TideService.cs`.
    - [x] Implement method for NOAA API request (today's date only).
    - [x] Implement JSON parsing and C# models for tide data.
    - [x] Implement filtering for tide height ≤ 2 feet and only show future times for today.
- [x] **Step 1.4: Integrate Tide Service with Backend Logic**
    - [x] Invoke `TideService` from Controller/PageModel on page load or user submission.
    - [x] Pass processed tide info to UI.
- [x] **Step 1.5: Configuration Management**
    - [x] Store API endpoints, etc., using `appsettings.json` or environment variables.
- [x] **Step 1.6: Add About Page**
    - [x] Create an About page describing the project purpose and technology stack.

## Phase 2: Freighter Data - AIS Provider Research & Initial API Setup
- [ ] **Step 2.1: Research and Select AIS Data Provider**
    - [ ] Evaluate providers (MarineTraffic, VesselFinder, AISHub, Spire Maritime, etc.).
    - [ ] Criteria: API capabilities (vessel type, location, COG, SOG, ETA), coverage, reliability, pricing, documentation.
    - [ ] Choose a provider.
- [ ] **Step 2.2: Obtain API Credentials & Setup Account**
    - [ ] Register and get API key/token.
- [ ] **Step 2.3: Develop Initial FreighterService.cs Structure**
    - [ ] Create `FreighterService.cs`.
    - [ ] Define initial method stubs (e.g., `async Task<FreighterData> GetSouthboundFreighterInfoAsync(...)`).
- [ ] **Step 2.4: Implement Basic AIS API Call**
    - [ ] Implement test API call within `FreighterService.cs`.
    - [ ] Authenticate and request data for Kingston, NY area.
    - [ ] Log raw JSON response.
- [ ] **Step 2.5: Model AIS Data Structures**
    - [ ] Define C# classes for AIS API JSON response.

## Phase 3: Core Freighter Logic - Southbound Filtering & ETA Calculation
- [ ] **Step 3.1: Define "Kingston Approach Zone" and "Southbound" Bearings**
    - [ ] Establish precise lat/lon for "Kingston Point".
    - [ ] Determine and document southbound bearing range (e.g., 160°-220° True).
- [ ] **Step 3.2: Implement Advanced Freighter Filtering in FreighterService.cs**
    - [ ] Filter by Vessel Type (freighters/cargo).
    - [ ] Filter by Proximity & Potential Path (north of Kingston, approaching).
    - [ ] Filter by Direction (COG/HDG within southbound range).
    - [ ] Filter out Vessels Already Passed Kingston.
- [ ] **Step 3.3: Implement Estimated Time of Arrival (ETA) Calculation**
    - [ ] Calculate distance to Kingston Point (Haversine formula).
    - [ ] Estimate time to arrival (Distance / SOG).
    - [ ] Calculate ETA (Current Time + Time to Arrival).
    - [ ] Consider using API-provided ETA if available.
- [ ] **Step 3.4: Handle Date-Specific Logic (Near-Term vs. Far-Future)**
    - [ ] For "Today"/"Tomorrow": Execute live AIS calls and logic.
    - [ ] For Future Dates: Return message (no live tracking).
- [ ] **Step 3.5: Integrate Filtered Freighter Data with UI**
    - [ ] Call `FreighterService` after tide check.
    - [ ] Combine and format tide and freighter data for UI display.

## Phase 4: UI/UX Enhancements & Application Robustness
- [ ] **Step 4.1: Refine Results Display**
    - [ ] Enhance visual presentation (headings, lists, tables, highlight critical data).
- [ ] **Step 4.2: Implement Loading Indicators & User Feedback**
    - [ ] Add visual cues during API calls/processing.
- [ ] **Step 4.3: Comprehensive Error Handling**
    - [ ] Implement try-catch blocks for API calls and critical logic.
    - [ ] Handle API errors, network issues, parsing errors, rate limiting.
    - [ ] Show user-friendly error messages.
- [ ] **Step 4.4: Input Validation**
    - [ ] Ensure basic validation on user inputs (e.g., date selected).

## Phase 5: Thorough Testing & Deployment
- [ ] **Step 5.1: Unit and Integration Testing**
    - [ ] Unit Tests: For `TideService` logic, ETA calculation, bearing checks.
    - [ ] Integration Tests: Service interactions, external API interactions (mocked or live).
- [ ] **Step 5.2: User Acceptance Testing (UAT)**
    - [ ] Test various dates, tide conditions, freighter scenarios.
    - [ ] Verify accuracy of tide info, freighter detection, ETAs, UI messages.
- [ ] **Step 5.3: Deployment Preparation**
    - [ ] Select hosting environment.
    - [ ] Configure production environment settings securely.
- [ ] **Step 5.4: Deploy Application**
    - [ ] Publish application to hosting platform.
    - [ ] Perform post-deployment testing.

## Phase 6: (Optional) Advanced Enhancements & Ongoing Maintenance
- [ ] **Step 6.1: Historical Freighter Data Analysis**
    - [ ] (Consider if feasible) Acquire, store, and analyze historical AIS data for pattern identification.
- [ ] **Step 6.2: User Interface/Experience (UI/UX) Upgrades**
    - [ ] (Optional) Interactive map display, user accounts/preferences.
- [ ] **Step 6.3: Implement Application Monitoring & Logging**
    - [ ] Integrate robust logging framework (Serilog, NLog).
    - [ ] Set up basic application monitoring.
- [ ] **Step 6.4: Regular Maintenance & Updates**
    - [ ] Keep framework and packages updated.
    - [ ] Monitor APIs for changes.
    - [ ] Review/renew API keys.