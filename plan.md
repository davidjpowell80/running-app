# RUN LEAGUE — MVP Implementation Plan

## Objective

Build the first working vertical slice of RUN LEAGUE and prove that we can:

1. Connect a runner's Strava account using OAuth.
2. Import recent running activities when the runner first connects.
3. Receive future Strava activity changes automatically using webhooks.
4. Retrieve detailed activity and sensor data available from Strava.
5. Persist imported data reliably and idempotently.
6. Present that data in a simple web application so we can inspect exactly what RUN LEAGUE can use later for competition scoring and run verification.

This phase is deliberately focused on the Strava integration and activity viewer. It is not yet the RUN LEAGUE competition product.

## MVP user journey

> Open RUN LEAGUE → Connect with Strava → Authorise access → Recent runs appear → Complete another run → Strava sends a webhook → RUN LEAGUE imports it automatically → Open the run and inspect activity, route and sensor data.

The first user will be the developer's own Strava account.

## Proposed technology

### Backend
- ASP.NET Core Web API
- Current supported .NET version
- Entity Framework Core

### Frontend
- React
- TypeScript

### Persistence
Use a relational database through EF Core.

For local development, SQLite is acceptable for simplicity. Keep the persistence layer portable so it can later move to Azure SQL or PostgreSQL without redesigning the application.

### External integration
- Strava API
- Strava OAuth 2.0
- Strava Webhooks

Azure is the likely later hosting target, but cloud deployment is not required to prove the first local vertical slice except where a publicly reachable HTTPS webhook endpoint is needed.

## High-level architecture

```text
                  +------------------+
                  |      Strava      |
                  +---------+--------+
                            |
                  OAuth / REST API
                            |
                            v
+-------------+      +------+-------+       +-------------+
| React Web   | ---> | ASP.NET Core | ----> |  Database   |
| Application | <--- |     API      | <---- |             |
+-------------+      +------+-------+       +-------------+
                            ^
                            |
                     Strava Webhook
                            |
                  +---------+--------+
                  |      Strava      |
                  +------------------+
```

The backend owns all communication with Strava. Client secrets, access tokens and refresh tokens must never be exposed to the browser.

## Strava developer application

Create/register a Strava API application with:
- Client ID
- Client Secret
- OAuth callback URL
- Webhook callback URL

Initially use the developer's own Strava account. The initial single-athlete development mode is sufficient for the prototype.

Secrets must be provided through development secrets/environment configuration and must not be committed to Git.

## OAuth flow

The web application displays **Connect with Strava**.

After the user authorises RUN LEAGUE, Strava redirects to the backend callback endpoint. The backend will:

1. Validate the OAuth state value.
2. Exchange the authorisation code for tokens.
3. Store the Strava athlete ID.
4. Store the access token securely.
5. Store the refresh token securely.
6. Store token expiry information.
7. Store useful athlete/profile information.
8. Trigger the initial activity import.

All Strava API calls should go through a dedicated Strava client/service responsible for checking token expiry and refreshing tokens when required. If Strava returns a new refresh token, persist the newest token.

## Initial activity synchronisation

Immediately after connecting Strava:

1. Retrieve a limited set of recent activities.
2. Select running activities relevant to RUN LEAGUE.
3. Fetch detailed activity data where required.
4. Fetch available activity streams.
5. Persist the activity and raw source data.

For the prototype, approximately the most recent 20 activities is sufficient. Make the number configurable.

## Strava webhooks

Webhooks are a first-class MVP requirement.

Implement:
- webhook subscription creation;
- verification/challenge endpoint;
- event receiver;
- create/update/delete handling for activity events.

The webhook endpoint should respond quickly. Separate receiving the event from slower Strava API processing so there is a clean path to adding a queue/background worker later.

```text
Receive event
    ↓
Persist/accept event
    ↓
Process event
    ↓
Call Strava
    ↓
Update local activity
```

## Idempotency

Treat webhook delivery as at-least-once.

Persist enough event information to determine whether a notification has already been received or processed. Processing the same notification more than once must not create duplicate activities or corrupt data.

Activity records should be uniquely associated with the Strava athlete and Strava activity ID.

## Activity ingestion

For a created or updated activity:

1. Identify the Strava athlete.
2. Ensure a valid access token exists.
3. Retrieve the activity from Strava.
4. Determine whether it is a running activity relevant to RUN LEAGUE.
5. Retrieve available streams.
6. Persist/update normalised activity data.
7. Persist raw API data useful for investigation.
8. Mark the webhook event as processed.

For a deleted activity, mark the corresponding local activity as deleted or remove it according to the chosen retention policy, while retaining useful audit/event information during development.

## Data to capture

Potential activity fields include:
- Strava activity ID
- athlete ID
- activity name
- sport/activity type
- start date/time
- local start date/time
- timezone
- distance
- moving time
- elapsed time
- total elevation gain
- average speed
- maximum speed
- average heart rate
- maximum heart rate
- average cadence
- power where available
- calories where available
- device/gear information where available
- start/end coordinates where available
- map/polyline information where available

The implementation must tolerate fields being absent because different runners and devices expose different sensor data.

## Activity streams

Retrieve available streams such as:
- time
- latitude/longitude
- distance
- altitude
- velocity/speed
- heart rate
- cadence
- moving state
- grade
- power
- temperature

Do not assume every stream exists for every activity.

These streams are important because they are likely to become inputs to the future RUN LEAGUE verification engine.

## Raw data

Store the raw Strava activity response and, where practical, the raw stream response and webhook payload.

The UI should include a **Raw Data** view so we can inspect the source data directly and answer questions such as:
- What device information does Strava expose?
- Which Garmin-recorded fields survive Garmin → Strava sync?
- Is heart rate available?
- Is cadence available?
- How detailed is the GPS stream?
- Can we identify the recording source?
- What data could later contribute to run verification?

## Initial persistence model

### Athlete
- Id
- StravaAthleteId
- FirstName
- LastName
- ProfileImageUrl
- CreatedAt
- UpdatedAt

### StravaConnection
- Id
- AthleteId
- AccessToken
- RefreshToken
- AccessTokenExpiresAt
- Scopes
- ConnectedAt
- UpdatedAt

### Activity
- Id
- AthleteId
- StravaActivityId
- ActivityType
- Name
- StartDate
- Distance
- MovingTime
- ElapsedTime
- ElevationGain
- AverageSpeed
- MaximumSpeed
- AverageHeartRate
- MaximumHeartRate
- AverageCadence
- IsDeleted
- ImportedAt
- UpdatedAt
- RawJson

### ActivityStream
For the first prototype, stream values can be stored as JSON unless querying individual samples becomes necessary.

- Id
- ActivityId
- StreamType
- RawJson
- ImportedAt

### WebhookEvent
- Id
- StravaObjectId
- ObjectType
- AspectType
- OwnerId
- EventTime
- RawJson
- ReceivedAt
- ProcessedAt
- ProcessingStatus
- Error

Add indexes/unique constraints for the important idempotency rules.

## Initial backend endpoints

```text
GET  /api/strava/connect
GET  /api/strava/callback
GET  /api/strava/webhook
POST /api/strava/webhook

GET  /api/athlete
GET  /api/activities
GET  /api/activities/{id}
GET  /api/activities/{id}/streams
GET  /api/activities/{id}/raw
```

Keep Strava-specific concerns behind application services rather than spreading Strava API models throughout the product domain.

## Web UI

### Connection page

```text
RUN LEAGUE

Connect your running activity

[ Connect with Strava ]
```

After connection, show basic athlete information and access to imported activities.

### Activity list

Display recent running activities with useful summary fields, for example:

| Date | Activity | Distance | Time | Pace | Heart Rate |
|---|---|---:|---:|---:|---:|

### Activity detail

Use sections/tabs such as:

```text
Overview | Route | Streams | Raw Data
```

Overview should show normalised statistics. Route should show the GPS route when available. Streams should allow inspection/visualisation of available data. Useful first charts are pace/speed, heart rate, elevation and cadence over time. Only display charts where the corresponding data exists.

## Local webhook development

Strava must be able to reach the webhook endpoint over the internet, so localhost alone is not enough.

Use a secure HTTPS development tunnel to expose the required callback endpoint during local development. Keep the public callback URL configurable because it will differ between local development and deployed environments.

Document the chosen tunnelling approach in the repository once implementation starts.

## Error handling and reliability

Handle explicitly:
- expired access tokens
- token refresh failures
- revoked Strava access
- Strava API errors
- rate limiting
- missing streams
- activities without GPS
- duplicate webhook events
- webhook processing failures
- deleted activities
- malformed/unexpected payloads

Do not silently discard failed webhook processing. Record failures so they can be inspected and retried.

## Strava rate limits

Treat Strava API calls as a constrained external resource.

Principles:
- use webhooks for ongoing change detection rather than polling;
- avoid repeatedly downloading unchanged activities;
- persist imported data locally;
- avoid repeatedly requesting streams;
- log rate-limit information where useful;
- make retry/backoff behaviour easy to add.

## Security and privacy

Minimum requirements:
- never commit the Strava client secret;
- never expose client secret/access token/refresh token to the frontend;
- use HTTPS for OAuth/webhook endpoints;
- validate OAuth state;
- validate webhook inputs appropriately;
- avoid logging access/refresh tokens;
- use development secret storage/environment variables;
- make future account disconnection/deletion straightforward;
- minimise requested Strava permissions.

Before onboarding external testers, revisit privacy, retention, deletion, consent and production token encryption.

## Implementation milestones

### Milestone 1 — Application skeleton and OAuth
- Create solution/repository structure.
- ASP.NET Core API.
- React/TypeScript frontend.
- Database and EF Core migrations.
- Configuration/secrets structure.
- Register Strava application.
- Implement Connect with Strava.
- OAuth callback.
- Persist athlete and connection/tokens.
- Implement token refresh.

**Outcome:** developer can connect their Strava account to RUN LEAGUE.

### Milestone 2 — Webhook integration
- Implement webhook verification endpoint.
- Expose callback publicly for development.
- Create Strava webhook subscription.
- Receive webhook events.
- Persist webhook events.
- Implement idempotency.

**Outcome:** RUN LEAGUE receives Strava activity notifications automatically.

### Milestone 3 — Activity ingestion
- Implement initial recent-activity sync.
- Implement detailed activity retrieval.
- Implement stream retrieval.
- Process create/update/delete webhook events.
- Persist normalised activity data.
- Persist raw payloads.

**Outcome:** activities arrive automatically and are stored locally.

### Milestone 4 — Activity viewer
- Athlete/connection screen.
- Activity list.
- Activity detail.
- Route map.
- Stream charts/data inspection.
- Raw JSON view.

**Outcome:** developer can inspect exactly what Strava provides for real runs recorded by their devices.

### Milestone 5 — Data assessment
Use several real activities recorded using the developer's normal devices and determine:
- which fields are consistently available;
- which sensor streams are available;
- device/source information available;
- GPS resolution and quality;
- HR/cadence availability;
- data differences between recording devices;
- candidate signals for run verification.

Document the findings before designing the competition engine.

## Definition of Done

The MVP integration prototype is complete when:

1. The RUN LEAGUE web application runs locally.
2. The developer can click **Connect with Strava**.
3. OAuth completes successfully against their Strava account.
4. Athlete/connection details are persisted.
5. Recent runs are imported automatically after initial connection.
6. A valid Strava webhook subscription exists.
7. A newly created Strava activity triggers the RUN LEAGUE webhook automatically.
8. Duplicate webhook delivery does not create duplicate data.
9. The application fetches and persists the corresponding detailed activity.
10. Available streams are fetched and persisted.
11. The activity appears in the UI without manual polling/import.
12. The developer can open the activity and inspect summary statistics.
13. The route can be inspected when GPS data is available.
14. Available HR/cadence/elevation/speed/etc. data can be inspected.
15. Raw Strava data can be inspected.
16. Tokens refresh correctly when required.
17. Secrets are not committed to source control.

At that point, stop adding features and assess the imported data before proceeding.

## Explicitly out of scope

Do **not** build these during this phase:
- paid subscriptions
- prize money
- competitions
- leagues
- rankings
- XP/levels
- medals/achievements
- teams/clubs
- friends/social features
- Daily Wheel
- AR gameplay
- AR glasses integration
- direct Garmin integration
- direct Apple Health integration
- proprietary Race Ring hardware
- sophisticated anti-cheat/ML
- global scaling architecture

## Next phase — Run eligibility and verification

Once the Strava integration has been proven and real data inspected, the likely next vertical slice is:

```text
Strava Activity
      ↓
RUN LEAGUE Activity Import
      ↓
Eligibility / Verification Engine
      ↓
Accepted Competition Run
      or
Rejected / Flagged Run
```

Start the verification engine with deterministic rules before considering ML.

Potential signals include:
- GPS continuity
- pace
- acceleration
- heart rate
- cadence
- elevation
- device/source information
- impossible movement
- duplicate activities
- historical runner profile

Only after this foundation works should the MVP expand into competitions, leaderboards, scoring and rewards.
