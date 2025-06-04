# Project Plan: Hudson River Southbound Freighter Wake Advisor

## Overall Goal
To develop an ASP.NET web application that identifies optimal times for wake surfing behind southbound freighters on the Hudson River near Kingston, NY, by correlating low tide conditions (≤ 2 feet) with predicted southbound freighter approaches. The application only supports tide predictions for today's date (no future date predictions).

---

## Phase 1: Core Setup & Tide Data Integration
**Objective:** Establish the project foundation, create a basic user interface, and implement reliable fetching and display of tide information from NOAA for Kingston, NY. The UI and backend are now restricted to only allow and process today's date.

### Step 1.1: Project Initialization & Environment Setup
**Action:** Create a new ASP.NET Core project (MVC, Razor Pages, or Blazor).
**Details:**
* Initialize a Git repository for version control.
* Install essential NuGet packages: `System.Net.Http` for API calls, and `Newtonsoft.Json` or `System.Text.Json` for JSON processing.
**Tools:** Visual Studio or VS Code.

### Step 1.2: Basic User Interface (UI) Design
**Action:** Develop a minimal frontend for user interaction.
**Key Elements:**
* A date input field/picker restricted to today's date only.
* A "Check Conditions" submit button.
* A clearly defined area to display results (tide status, freighter information).
**Focus:** Prioritize functionality over aesthetics at this stage.

### Step 1.3: Implement Tide Service (`TideService.cs`)
**Action:** Create a dedicated C# service class to encapsulate all tide-related logic.
**Core Functionality:**
* Method to accept only today's date and location (Kingston, NY; NOAA Station ID `8519482`).
* Construct the API request URL for the `NOAA CO-OPS API` for today only.
* Execute an HTTP GET request to the NOAA API.
* Parse the JSON response, creating C# models to represent the tide data structure.
* Filter tide predictions to identify time windows when the tide height is at or below 2 feet and only show future times for today.
**Output:** A list of suitable low-tide windows or a message if none are found.

### Step 1.4: Integrate Tide Service with Backend Logic
**Action:** In your ASP.NET Core Controller or PageModel, invoke the `TideService` upon user submission of a date.
**Display:** Pass the processed tide information to the UI for display (e.g., "Low tide periods on [Selected Date]: 10:00 AM - 11:30 AM (1.8ft)").

### Step 1.5: Configuration Management
**Action:** Securely store any necessary configuration values (e.g., API endpoints, future API keys).
**Method:** Utilize ASP.NET Core's configuration system (`appsettings.json`, environment variables, user secrets for development).

### Step 1.6: Add About Page
**Action:** Create an About page that describes the project, its purpose, and the technology stack used.
**Details:**
* The About page should be accessible from the main navigation.
* It should explain the goal of the project and its educational/recreational context.

---

## Phase 2: Freighter Data - AIS Provider Research & Initial API Setup
**Objective:** Investigate and select an AIS (Automatic Identification System) data provider, establish API access, and perform initial API calls to understand the data structure and availability for freighters near Kingston.

### Step 2.1: Research and Select AIS Data Provider
**Action:** Evaluate potential AIS data providers (e.g., MarineTraffic API, VesselFinder API, AISHub, Spire Maritime, etc.).
**Key Evaluation Criteria:**
* API capabilities: Must provide vessel type, current location (lat/lon), Course Over Ground (COG) or Heading (HDG), Speed Over Ground (SOG). ETA data is a plus.
* Data coverage and reliability for the Hudson River, specifically near Kingston.
* Pricing structure, subscription costs, and any available free/developer tiers.
* Quality and clarity of API documentation.
**Decision:** Choose a provider that balances features, reliability, and cost for your project.

### Step 2.2: Obtain API Credentials & Setup Account
**Action:** Register with the chosen AIS service and acquire the necessary API key(s) or access tokens.

### Step 2.3: Develop Initial `FreighterService.cs` Structure
**Action:** Create a new C# service class (`FreighterService.cs`) to handle all freighter-related logic.
**Initial Methods (Stubs):**
* `async Task<FreighterData> GetSouthboundFreighterInfoAsync(DateTime selectedDate, GeoPoint kingstonReference)`

### Step 2.4: Implement Basic AIS API Call
**Action:** Within `FreighterService.cs`, implement a method to make a test API call to your chosen AIS provider.
**Focus:**
* Successfully authenticate with the AIS API.
* Request vessel data for a geographic area encompassing Kingston, NY.
* Retrieve and log the raw JSON response to thoroughly understand its structure and the data fields available.

### Step 2.5: Model AIS Data Structures
**Action:** Define C# classes (POCOs) that accurately represent the structure of the AIS API's JSON response for vessel information. This will facilitate easy deserialization.

---

## Phase 3: Core Freighter Logic - Southbound Filtering & ETA Calculation
**Objective:** Implement the primary logic to identify southbound freighters approaching Kingston and estimate their arrival times, considering the selected date.

### Step 3.1: Define "Kingston Approach Zone" and "Southbound" Bearings
**Kingston Point:** Establish a precise latitude/longitude coordinate for "Kingston" that freighters would pass.
**Southbound Bearings:** Consult nautical charts or detailed maps of the Hudson River near Kingston to determine the typical compass bearing range (e.g., 160° to 220° True) that defines "southbound" traffic in the main shipping channel. Document this range clearly.

### Step 3.2: Implement Advanced Freighter Filtering in `FreighterService.cs`
**Action:** Enhance `FreighterService.cs` to process the AIS data:
* **Filter by Vessel Type:** Isolate actual freighters/cargo ships from other vessel types (e.g., recreational boats, tugs) using the vessel type codes provided in the AIS data.
* **Filter by Proximity & Potential Path:** Select vessels currently located north of your Kingston reference point and within a reasonable distance to be considered "approaching."
* **Filter by Direction (Crucial):** Implement logic to include only those vessels whose Course Over Ground (COG) or Heading (HDG) falls within your predefined "southbound" bearing range.
* **Filter out Vessels Already Passed:** Ensure your logic correctly excludes freighters that are already south of your Kingston reference point.

### Step 3.3: Implement Estimated Time of Arrival (ETA) Calculation
**Action:** For each filtered, relevant southbound freighter:
* Calculate the distance from its current position to your Kingston reference point (using Haversine formula or similar).
* Estimate time to arrival: Time (hours) = Distance (nautical miles) / Speed Over Ground (knots).
* Calculate ETA: ETA = Current Time + Calculated Time to Arrival.
**Note:** If the AIS API provides a direct ETA to a relevant port or waypoint, consider using or cross-referencing it.

### Step 3.4: Handle Date-Specific Logic (Near-Term vs. Far-Future)
**Action:** Differentiate behavior based on the user's selected date:
* **For "Today"** (or a very short window like 24 hours): Execute the live AIS API calls and the full filtering/ETA calculation logic.
* **For Dates Beyond Today:** Do not attempt live AIS calls. Instead, return a clear message to the user indicating that specific freighter predictions are not feasible (e.g., "Live freighter tracking is only available for today.").

### Step 3.5: Integrate Filtered Freighter Data with UI
**Action:** In the Controller/PageModel, after confirming suitable low tide conditions:
* Call the `FreighterService` to get southbound freighter information.
* Combine and format the tide and freighter data for clear presentation in the UI.
**Example Output:** "Low tide (1.8ft) expected 10:00-11:30 AM. Southbound freighter 'Hudson Voyager' estimated near Kingston around 10:45 AM." or "...No southbound freighters detected during low tide periods."

---

## Phase 4: UI/UX Enhancements & Application Robustness
**Objective:** Improve the overall user experience, provide clear feedback during operations, and ensure the application handles potential errors gracefully.

### Step 4.1: Refine Results Display
**Action:** Enhance the visual presentation of tide and freighter information.
**Considerations:** Use clear headings, lists, or tables. Highlight critical data points (e.g., specific low tide times, freighter names, ETAs).

### Step 4.2: Implement Loading Indicators & User Feedback
**Action:** Add visual cues (e.g., spinners, progress messages) to indicate when the application is fetching data from external APIs ("Fetching tide data...", "Analyzing freighter traffic...").
**Benefit:** Improves perceived performance and keeps the user informed.

### Step 4.3: Comprehensive Error Handling
**Action:** Implement robust `try-catch` blocks around all API calls and critical processing logic.
**Specific Errors to Handle:**
* API unavailability or server errors (e.g., HTTP 5xx).
* Client-side errors (e.g., HTTP 4xx, invalid API keys).
* Network timeouts or connectivity issues.
* Unexpected data formats or parsing errors from API responses.
* API rate limiting issues.
**User Display:** Show user-friendly error messages (e.g., "Could not retrieve tide data at this time. Please try again later.") instead of technical error details.

### Step 4.4: Input Validation
**Action:** Ensure basic validation on user inputs (e.g., a date must be selected before submission).

---

## Phase 5: Thorough Testing & Deployment
**Objective:** Rigorously test all aspects of the application and deploy it to a suitable hosting environment.

### Step 5.1: Unit and Integration Testing
**Unit Tests:** Write tests for individual methods and logic units within your services (e.g., tide filtering logic, ETA calculation, southbound bearing checks).
**Integration Tests:** Test the interaction between services (e.g., ensuring the `TideService` output correctly triggers `FreighterService` calls) and with (mocked or live, if feasible) external APIs.

### Step 5.2: User Acceptance Testing (UAT)
**Action:** As the primary user (or with other potential users), conduct thorough end-to-end testing.
**Scenarios to Test:**
* Today's date with various tide conditions.
* (If possible during testing with live AIS) Scenarios where southbound freighters are expected/not expected.
**Verification:** Confirm accuracy of tide info, freighter detection (especially southbound logic), ETA calculations, and all UI messages.

### Step 5.3: Deployment Preparation
**Action:** Select a hosting environment (e.g., Azure App Service, AWS Elastic Beanstalk, a Virtual Private Server, or local IIS for personal use).
**Action:** Configure production environment settings, especially for API keys and any other sensitive data, using secure methods (environment variables, Azure Key Vault, etc.).

### Step 5.4: Deploy Application
**Action:** Publish your ASP.NET Core application to the chosen hosting platform.
**Action:** Perform post-deployment testing to ensure everything functions correctly in the live environment.

---

## Phase 6: (Optional) Advanced Enhancements & Ongoing Maintenance
**Objective:** Consider potential future improvements and plan for the long-term upkeep of the application.

### Step 6.1: Historical Freighter Data Analysis (Significant Optional Feature)
**Concept:** To provide any probabilistic guidance for far-future dates (beyond "check back later").
**Effort:** This would involve acquiring historical AIS data (potentially costly and complex), storing it, and performing data analysis to identify general patterns of southbound freighter traffic near Kingston (e.g., common days/times, correlation with seasons or tides).
**Integration:** If successful, these generalized insights could be presented for future dates.

### Step 6.2: User Interface/Experience (UI/UX) Upgrades
**Ideas:**
* Interactive map display (using libraries like `Leaflet.js` or `OpenLayers`) to visualize freighter locations (requires more sophisticated AIS data handling).
* User accounts and preferences (e.g., saved locations, notification settings if you were to add alerts).

### Step 6.3: Implement Application Monitoring & Logging
**Logging:** Integrate a robust logging framework (e.g., Serilog, NLog) to record application events, errors, and performance metrics in production.
**Monitoring:** Set up basic monitoring for application uptime, response times, and error rates.

### Step 6.4: Regular Maintenance & Updates
**Actions:**
* Keep the ASP.NET Core framework and all NuGet packages updated to their latest stable versions for security and bug fixes.
* Monitor the NOAA and AIS APIs for any changes, deprecations, or updates to their terms of service.
* Periodically review and renew API keys or subscriptions as needed.

This comprehensive plan should provide a clear roadmap for your project. The most challenging aspects will likely be the reliable integration with AIS data (Phase 2 & 3) due to external dependencies and potential costs, and the inherent limitations of long-range freighter prediction. Good luck with your development!